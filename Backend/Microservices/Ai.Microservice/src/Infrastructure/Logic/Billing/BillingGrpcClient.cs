using System.Globalization;
using Application.Abstractions.Billing;
using Grpc.Core;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Grpc.UserBilling;

namespace Infrastructure.Logic.Billing;

public sealed class BillingGrpcClient : IBillingClient
{
    private readonly UserBillingService.UserBillingServiceClient _client;

    public BillingGrpcClient(UserBillingService.UserBillingServiceClient client)
    {
        _client = client;
    }

    public async Task<Result<decimal>> GetBalanceAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetBalanceAsync(
                new GetBalanceRequest { UserId = userId.ToString() },
                cancellationToken: cancellationToken);
            return Result.Success(ParseDecimal(response.Balance));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return Result.Failure<decimal>(new Error(BillingClientErrors.UserNotFound, ex.Status.Detail));
        }
        catch (RpcException ex)
        {
            return Result.Failure<decimal>(new Error("Billing.Unavailable", ex.Status.Detail));
        }
    }

    public async Task<Result<decimal>> DebitAsync(
        Guid userId,
        decimal amount,
        string reason,
        string referenceType,
        string referenceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.DebitCoinsAsync(
                new DebitCoinsRequest
                {
                    UserId = userId.ToString(),
                    Amount = amount.ToString(CultureInfo.InvariantCulture),
                    Reason = reason,
                    ReferenceType = referenceType,
                    ReferenceId = referenceId
                },
                cancellationToken: cancellationToken);

            if (!response.Success)
            {
                // Propagate InsufficientFunds / UserNotFound / InvalidAmount verbatim so
                // the caller (command handler) can translate to HTTP 402 when appropriate.
                return Result.Failure<decimal>(new Error(response.ErrorCode, response.ErrorMessage));
            }

            return Result.Success(ParseDecimal(response.NewBalance));
        }
        catch (RpcException ex)
        {
            return Result.Failure<decimal>(new Error("Billing.Unavailable", ex.Status.Detail));
        }
    }

    public async Task<Result<decimal>> RefundAsync(
        Guid userId,
        decimal amount,
        string reason,
        string referenceType,
        string referenceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.RefundCoinsAsync(
                new RefundCoinsRequest
                {
                    UserId = userId.ToString(),
                    Amount = amount.ToString(CultureInfo.InvariantCulture),
                    Reason = reason,
                    ReferenceType = referenceType,
                    ReferenceId = referenceId
                },
                cancellationToken: cancellationToken);

            if (!response.Success)
            {
                return Result.Failure<decimal>(new Error(response.ErrorCode, response.ErrorMessage));
            }

            return Result.Success(ParseDecimal(response.NewBalance));
        }
        catch (RpcException ex)
        {
            return Result.Failure<decimal>(new Error("Billing.Unavailable", ex.Status.Detail));
        }
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }
}
