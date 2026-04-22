using Application.Abstractions.Billing;
using Domain.Entities;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Extensions;

namespace Infrastructure.Logic.Services;

public sealed class BillingService : IBillingService
{
    private readonly MyDbContext _dbContext;

    public BillingService(MyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<decimal>> GetBalanceAsync(Guid userId, CancellationToken cancellationToken)
    {
        var balance = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeleted)
            .Select(u => u.MeAiCoin)
            .FirstOrDefaultAsync(cancellationToken);

        if (balance is null && !await _dbContext.Users.AnyAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken))
        {
            return Result.Failure<decimal>(new Error(BillingErrors.UserNotFound, "User not found."));
        }

        return Result.Success(balance ?? 0m);
    }

    public async Task<Result<decimal>> DebitAsync(
        Guid userId,
        decimal amount,
        string reason,
        string? referenceType,
        string? referenceId,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
        {
            return Result.Failure<decimal>(
                new Error(BillingErrors.InvalidAmount, "Debit amount must be positive."));
        }

        // Row-level lock on the user row so concurrent debits can't both pass the balance
        // check (classic TOCTOU) and leave us negative. PostgreSQL `FOR UPDATE` inside a
        // transaction is the minimum required here.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, cancellationToken);

        var user = await _dbContext.Users
            .FromSqlRaw("SELECT * FROM users WHERE id = {0} FOR UPDATE", userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null || user.IsDeleted)
        {
            return Result.Failure<decimal>(new Error(BillingErrors.UserNotFound, "User not found."));
        }

        var balance = user.MeAiCoin ?? 0m;
        if (balance < amount)
        {
            return Result.Failure<decimal>(
                new Error(
                    BillingErrors.InsufficientFunds,
                    $"Insufficient MeAI coins — need {amount}, have {balance}."));
        }

        var newBalance = balance - amount;
        user.MeAiCoin = newBalance;
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _dbContext.CoinTransactions.Add(new CoinTransaction
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Delta = -amount,
            Reason = reason,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            BalanceAfter = newBalance,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(newBalance);
    }

    public async Task<Result<RefundResult>> RefundAsync(
        Guid userId,
        decimal amount,
        string reason,
        string? referenceType,
        string? referenceId,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
        {
            return Result.Failure<RefundResult>(
                new Error(BillingErrors.InvalidAmount, "Refund amount must be positive."));
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, cancellationToken);

        // Dedupe: if a ledger entry with this exact (reason, refType, refId) already exists,
        // don't re-apply. Consumers may retry after transient failures and we'd otherwise
        // refund the same charge multiple times.
        if (!string.IsNullOrWhiteSpace(referenceId))
        {
            var already = await _dbContext.CoinTransactions
                .AsNoTracking()
                .AnyAsync(
                    t => t.UserId == userId
                        && t.Reason == reason
                        && t.ReferenceType == referenceType
                        && t.ReferenceId == referenceId,
                    cancellationToken);

            if (already)
            {
                var currentBalance = await _dbContext.Users
                    .AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => u.MeAiCoin ?? 0m)
                    .FirstOrDefaultAsync(cancellationToken);

                return Result.Success(new RefundResult(currentBalance, AlreadyApplied: true));
            }
        }

        var user = await _dbContext.Users
            .FromSqlRaw("SELECT * FROM users WHERE id = {0} FOR UPDATE", userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return Result.Failure<RefundResult>(new Error(BillingErrors.UserNotFound, "User not found."));
        }

        var newBalance = (user.MeAiCoin ?? 0m) + amount;
        user.MeAiCoin = newBalance;
        user.UpdatedAt = DateTimeExtensions.PostgreSqlUtcNow;

        _dbContext.CoinTransactions.Add(new CoinTransaction
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Delta = amount,
            Reason = reason,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            BalanceAfter = newBalance,
            CreatedAt = DateTimeExtensions.PostgreSqlUtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(new RefundResult(newBalance, AlreadyApplied: false));
    }
}
