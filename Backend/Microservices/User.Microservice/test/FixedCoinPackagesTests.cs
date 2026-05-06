using System.Text.Json;
using Application.Abstractions.Billing;
using Application.Abstractions.Data;
using Application.Abstractions.Payments;
using Application.Billing.Commands;
using Application.Billing.Models;
using Application.Billing.Queries;
using Application.Billing.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Logic.Seeding;
using Infrastructure.Logic.Services;
using Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class FixedCoinPackagesTests : IDisposable
{
    private readonly List<SqliteConnection> _openConnections = [];

    [Fact]
    public async Task GetCoinPackagesQuery_ReturnsOnlyActivePackages()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CoinPackages.AddRange(
            CreateCoinPackage(Guid.NewGuid(), "Inactive", 100m, 0m, 19900m, false, 0),
            CreateCoinPackage(Guid.NewGuid(), "Starter", 500m, 50m, 49900m, true, 2),
            CreateCoinPackage(Guid.NewGuid(), "Basic", 250m, 25m, 29900m, true, 1));
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var handler = new GetCoinPackagesQueryHandler(unitOfWork);

        var result = await handler.Handle(new GetCoinPackagesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(item => item.Name).Should().ContainInOrder("Basic", "Starter");
        result.Value.Should().OnlyContain(item => item.Currency == "vnd");
        result.Value[0].TotalCoins.Should().Be(275m);
        result.Value[1].TotalCoins.Should().Be(550m);
    }

    [Fact]
    public async Task PurchaseCoinPackageCommand_CreatesPendingTransactionAndPaymentIntent()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(Guid.NewGuid(), "buyer", "buyer@example.com");
        var package = CreateCoinPackage(Guid.NewGuid(), "500 Coins", 500m, 50m, 49900m, true, 1);
        dbContext.Users.Add(user);
        dbContext.CoinPackages.Add(package);
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService
            .Setup(service => service.CreateCustomerAsync(
                user.Email,
                user.FullName,
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeCustomerResult("cus_coin_package"));
        stripePaymentService
            .Setup(service => service.CreateCoinPackagePaymentIntentAsync(
                "cus_coin_package",
                user.Email,
                user.FullName,
                package.Price,
                package.Currency,
                package.Name,
                It.Is<IDictionary<string, string>>(metadata =>
                    metadata["flow_type"] == "coin_package" &&
                    metadata["user_id"] == user.Id.ToString() &&
                    metadata["coin_package_id"] == package.Id.ToString()),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeOneTimePaymentResult(
                "pi_coin_package",
                "secret_coin_package",
                "requires_payment_method",
                "vnd",
                49900m));

        var stripeCustomerResolver = new StripeCustomerResolver(unitOfWork, stripePaymentService.Object);
        var handler = new PurchaseCoinPackageCommandHandler(unitOfWork, stripeCustomerResolver, stripePaymentService.Object);

        var result = await handler.Handle(new PurchaseCoinPackageCommand(package.Id, user.Id), CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.PackageId.Should().Be(package.Id);
        result.Value.PaymentIntentId.Should().Be("pi_coin_package");
        result.Value.ClientSecret.Should().Be("secret_coin_package");
        result.Value.Status.Should().Be("requires_payment_method");
        result.Value.AmountDue.Should().Be(49900m);
        result.Value.Currency.Should().Be("vnd");

        var transaction = await dbContext.Transactions.SingleAsync();
        transaction.UserId.Should().Be(user.Id);
        transaction.RelationId.Should().Be(package.Id);
        transaction.RelationType.Should().Be("CoinPackage");
        transaction.TransactionType.Should().Be("coin_package_purchase");
        transaction.PaymentMethod.Should().Be("Stripe");
        transaction.ProviderReferenceId.Should().Be("pi_coin_package");
        transaction.Status.Should().Be("requires_payment_method");
        transaction.Cost.Should().Be(49900m);

        var persistedUser = await dbContext.Users.SingleAsync();
        persistedUser.StripeCustomerId.Should().Be("cus_coin_package");
    }

    [Fact]
    public async Task PurchaseCoinPackageCommand_FailsForInactivePackage()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(Guid.NewGuid(), "buyer-inactive", "buyer-inactive@example.com");
        var package = CreateCoinPackage(Guid.NewGuid(), "Inactive Coins", 500m, 50m, 49900m, false, 1);
        dbContext.Users.Add(user);
        dbContext.CoinPackages.Add(package);
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var stripePaymentService = new Mock<IStripePaymentService>(MockBehavior.Strict);
        var stripeCustomerResolver = new StripeCustomerResolver(unitOfWork, stripePaymentService.Object);
        var handler = new PurchaseCoinPackageCommandHandler(unitOfWork, stripeCustomerResolver, stripePaymentService.Object);

        var result = await handler.Handle(new PurchaseCoinPackageCommand(package.Id, user.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CoinPackage.NotFound");
        (await dbContext.Transactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ResolveCheckout_SucceedsAndCreditsCoinsOnlyOnceAcrossResolveAndWebhook()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(Guid.NewGuid(), "resolver", "resolver@example.com");
        var package = CreateCoinPackage(Guid.NewGuid(), "1000 Coins", 1000m, 200m, 99900m, true, 1);
        var transaction = CreateTransaction(Guid.NewGuid(), user.Id, package.Id, package.Price, "pending", null);
        dbContext.Users.Add(user);
        dbContext.CoinPackages.Add(package);
        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var refundCalls = 0;
        var billingService = new Mock<IBillingService>();
        billingService
            .Setup(service => service.RefundAsync(
                user.Id,
                1200m,
                "coin_package.purchase",
                "coin_package",
                transaction.Id.ToString(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                refundCalls++;
                user.MeAiCoin = 1200m;
                if (refundCalls == 1)
                {
                    dbContext.CoinTransactions.Add(new CoinTransaction
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = user.Id,
                        Delta = 1200m,
                        Reason = "coin_package.purchase",
                        ReferenceType = "coin_package",
                        ReferenceId = transaction.Id.ToString(),
                        BalanceAfter = 1200m,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                return Result.Success(new RefundResult(1200m, AlreadyApplied: refundCalls > 1));
            });
        var confirmHandler = new ConfirmCoinPackagePaymentCommandHandler(unitOfWork, billingService.Object);
        var sender = new Mock<MediatR.ISender>();
        sender
            .Setup(service => service.Send(It.IsAny<ConfirmCoinPackagePaymentCommand>(), It.IsAny<CancellationToken>()))
            .Returns((ConfirmCoinPackagePaymentCommand command, CancellationToken ct) => confirmHandler.Handle(command, ct));

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService
            .Setup(service => service.GetCoinPackageCheckoutStatusAsync("pi_success", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeCheckoutStatusResult("succeeded", true, true, "pi_success", "pi_success", null));

        var resolveHandler = new ResolveCoinPackageCheckoutCommandHandler(stripePaymentService.Object, sender.Object);

        var firstResolve = await resolveHandler.Handle(
            new ResolveCoinPackageCheckoutCommand(user.Id, package.Id, transaction.Id, "pi_success"),
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        firstResolve.IsSuccess.Should().BeTrue();
        firstResolve.Value.IsFinal.Should().BeTrue();
        firstResolve.Value.CoinsCredited.Should().BeTrue();
        firstResolve.Value.AlreadyCredited.Should().BeFalse();
        firstResolve.Value.CreditedCoins.Should().Be(1200m);
        firstResolve.Value.CurrentBalance.Should().Be(1200m);

        var webhookReplay = await confirmHandler.Handle(
            new ConfirmCoinPackagePaymentCommand(user.Id, package.Id, transaction.Id, "pi_success", "succeeded"),
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        webhookReplay.IsSuccess.Should().BeTrue();
        webhookReplay.Value.CoinsCredited.Should().BeFalse();
        webhookReplay.Value.AlreadyCredited.Should().BeTrue();
        webhookReplay.Value.CreditedCoins.Should().Be(1200m);
        webhookReplay.Value.CurrentBalance.Should().Be(1200m);

        var persistedUser = await dbContext.Users.SingleAsync();
        persistedUser.MeAiCoin.Should().Be(1200m);

        var persistedTransaction = await dbContext.Transactions.SingleAsync();
        persistedTransaction.Status.Should().Be("succeeded");
        persistedTransaction.ProviderReferenceId.Should().Be("pi_success");

        var ledgerEntries = await dbContext.CoinTransactions.OrderBy(item => item.CreatedAt).ToListAsync();
        ledgerEntries.Should().HaveCount(1);
        ledgerEntries[0].Delta.Should().Be(1200m);
        ledgerEntries[0].Reason.Should().Be("coin_package.purchase");
        ledgerEntries[0].ReferenceType.Should().Be("coin_package");
        ledgerEntries[0].ReferenceId.Should().Be(transaction.Id.ToString());
        ledgerEntries[0].BalanceAfter.Should().Be(1200m);
    }

    [Fact]
    public async Task ResolveCheckout_WhenRepeated_DoesNotDoubleCredit()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(Guid.NewGuid(), "repeat", "repeat@example.com");
        var package = CreateCoinPackage(Guid.NewGuid(), "250 Coins", 250m, 25m, 29900m, true, 1);
        var transaction = CreateTransaction(Guid.NewGuid(), user.Id, package.Id, package.Price, "pending", null);
        dbContext.Users.Add(user);
        dbContext.CoinPackages.Add(package);
        dbContext.Transactions.Add(transaction);
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var refundCalls = 0;
        var billingService = new Mock<IBillingService>();
        billingService
            .Setup(service => service.RefundAsync(
                user.Id,
                275m,
                "coin_package.purchase",
                "coin_package",
                transaction.Id.ToString(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                refundCalls++;
                user.MeAiCoin = 275m;
                if (refundCalls == 1)
                {
                    dbContext.CoinTransactions.Add(new CoinTransaction
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = user.Id,
                        Delta = 275m,
                        Reason = "coin_package.purchase",
                        ReferenceType = "coin_package",
                        ReferenceId = transaction.Id.ToString(),
                        BalanceAfter = 275m,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                return Result.Success(new RefundResult(275m, AlreadyApplied: refundCalls > 1));
            });
        var confirmHandler = new ConfirmCoinPackagePaymentCommandHandler(unitOfWork, billingService.Object);
        var sender = new Mock<MediatR.ISender>();
        sender
            .Setup(service => service.Send(It.IsAny<ConfirmCoinPackagePaymentCommand>(), It.IsAny<CancellationToken>()))
            .Returns((ConfirmCoinPackagePaymentCommand command, CancellationToken ct) => confirmHandler.Handle(command, ct));

        var stripePaymentService = new Mock<IStripePaymentService>();
        stripePaymentService
            .Setup(service => service.GetCoinPackageCheckoutStatusAsync("pi_repeat", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StripeCheckoutStatusResult("succeeded", true, true, "pi_repeat", "pi_repeat", null));

        var handler = new ResolveCoinPackageCheckoutCommandHandler(stripePaymentService.Object, sender.Object);

        var first = await handler.Handle(
            new ResolveCoinPackageCheckoutCommand(user.Id, package.Id, transaction.Id, "pi_repeat"),
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var second = await handler.Handle(
            new ResolveCoinPackageCheckoutCommand(user.Id, package.Id, transaction.Id, "pi_repeat"),
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value.CoinsCredited.Should().BeTrue();
        second.Value.CoinsCredited.Should().BeFalse();
        second.Value.AlreadyCredited.Should().BeTrue();
        second.Value.CurrentBalance.Should().Be(275m);

        var persistedUser = await dbContext.Users.SingleAsync();
        persistedUser.MeAiCoin.Should().Be(275m);
        (await dbContext.CoinTransactions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AdminCoinPackageCommands_CreateUpdateAndDeactivatePackage()
    {
        await using var dbContext = CreateDbContext();
        using var unitOfWork = new UnitOfWork(dbContext);

        var createHandler = new CreateCoinPackageCommandHandler(unitOfWork);
        var createResult = await createHandler.Handle(
            new CreateCoinPackageCommand("Starter", 300m, 30m, 34900m, "vnd", true, 1),
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        createResult.IsSuccess.Should().BeTrue();
        createResult.Value.Name.Should().Be("Starter");
        createResult.Value.IsActive.Should().BeTrue();

        var updateHandler = new UpdateCoinPackageCommandHandler(unitOfWork);
        var updateResult = await updateHandler.Handle(
            new UpdateCoinPackageCommand(createResult.Value.Id, "Starter Plus", 400m, 80m, 44900m, "vnd", true, 3),
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        updateResult.IsSuccess.Should().BeTrue();
        updateResult.Value.Name.Should().Be("Starter Plus");
        updateResult.Value.CoinAmount.Should().Be(400m);
        updateResult.Value.BonusCoins.Should().Be(80m);
        updateResult.Value.Price.Should().Be(44900m);
        updateResult.Value.DisplayOrder.Should().Be(3);
        updateResult.Value.UpdatedAt.Should().NotBeNull();

        var deleteHandler = new DeleteCoinPackageCommandHandler(unitOfWork);
        var deleteResult = await deleteHandler.Handle(new DeleteCoinPackageCommand(createResult.Value.Id), CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        deleteResult.IsSuccess.Should().BeTrue();

        var persistedPackage = await dbContext.CoinPackages.SingleAsync();
        persistedPackage.IsActive.Should().BeFalse();
        persistedPackage.Name.Should().Be("Starter Plus");
    }

    [Fact]
    public async Task CoinPackageSeeder_SeedsCatalogPackagesOnce()
    {
        await using var dbContext = CreateDbContext();
        var seeder = new CoinPackageSeeder(dbContext, NullLogger<CoinPackageSeeder>.Instance);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var packages = await dbContext.CoinPackages
            .OrderBy(item => item.DisplayOrder)
            .ToListAsync();

        packages.Should().HaveCount(3);
        packages.Select(item => item.Name).Should().ContainInOrder(
            "Coin Package 10000",
            "Coin Package 15000",
            "Coin Package 20000");
        packages.Select(item => item.CoinAmount).Should().ContainInOrder(10000m, 15000m, 20000m);
        packages.Select(item => item.Price).Should().ContainInOrder(100000m, 150000m, 200000m);
        packages.Should().OnlyContain(item => item.Currency == "vnd");
    }

    [Fact]
    public async Task GetAdminCoinPackagesQuery_ReturnsActiveAndInactivePackages()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CoinPackages.AddRange(
            CreateCoinPackage(Guid.NewGuid(), "Inactive", 100m, 0m, 19900m, false, 0),
            CreateCoinPackage(Guid.NewGuid(), "Active", 250m, 25m, 29900m, true, 1));
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var handler = new GetAdminCoinPackagesQueryHandler(unitOfWork);

        var result = await handler.Handle(new GetAdminCoinPackagesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(item => item.Name).Should().ContainInOrder("Inactive", "Active");
        result.Value.Select(item => item.IsActive).Should().ContainInOrder(false, true);
    }

    private MyDbContext CreateDbContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        _openConnections.Add(connection);

        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new FixedCoinPackagesTestDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private sealed class FixedCoinPackagesTestDbContext : MyDbContext
    {
        public FixedCoinPackagesTestDbContext(DbContextOptions<MyDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<SocialMedia>()
                .Property(entity => entity.Metadata)
                .HasConversion(
                    document => document == null ? null : document.RootElement.GetRawText(),
                    json => string.IsNullOrWhiteSpace(json) ? null : JsonDocument.Parse(json));
        }
    }

    public void Dispose()
    {
        foreach (var connection in _openConnections)
        {
            connection.Dispose();
        }

        _openConnections.Clear();
    }

    private static User CreateUser(Guid userId, string username, string email) =>
        new()
        {
            Id = userId,
            Username = username,
            PasswordHash = "hash",
            Email = email,
            FullName = username,
            CreatedAt = DateTime.UtcNow,
            MeAiCoin = 0m,
            IsDeleted = false
        };

    private static CoinPackage CreateCoinPackage(
        Guid id,
        string name,
        decimal coinAmount,
        decimal bonusCoins,
        decimal price,
        bool isActive,
        int displayOrder) =>
        new()
        {
            Id = id,
            Name = name,
            CoinAmount = coinAmount,
            BonusCoins = bonusCoins,
            Price = price,
            Currency = "vnd",
            IsActive = isActive,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow
        };

    private static Transaction CreateTransaction(
        Guid id,
        Guid userId,
        Guid packageId,
        decimal price,
        string status,
        string? providerReferenceId) =>
        new()
        {
            Id = id,
            UserId = userId,
            RelationId = packageId,
            RelationType = "CoinPackage",
            Cost = price,
            TransactionType = "coin_package_purchase",
            PaymentMethod = "Stripe",
            Status = status,
            ProviderReferenceId = providerReferenceId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
}
