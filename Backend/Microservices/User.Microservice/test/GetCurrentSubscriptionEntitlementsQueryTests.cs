using System.Text.Json;
using Application.Abstractions.Data;
using Application.Subscriptions.Models;
using Application.Subscriptions.Queries;
using Application.Subscriptions.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace test;

public sealed class GetCurrentSubscriptionEntitlementsQueryTests
{
    [Fact]
    public async Task Handle_FreeTierUserWithoutLinkedSocials_ReturnsFallbackLimits()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();

        dbContext.Users.Add(CreateUser(userId, "free-user", "free@example.com"));
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var handler = CreateHandler(unitOfWork);

        var result = await handler.Handle(
            new GetCurrentSubscriptionEntitlementsQuery(userId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new CurrentSubscriptionEntitlementsResponse(
            HasActivePlan: false,
            CurrentSubscriptionId: null,
            CurrentPlanId: null,
            CurrentPlanName: null,
            MaxSocialAccounts: 2,
            CurrentSocialAccounts: 0,
            RemainingSocialAccounts: 2,
            MaxPagesPerSocialAccount: 5,
            CurrentWorkspaceCount: 0,
            MaxWorkspaces: int.MaxValue));
    }

    [Fact]
    public async Task Handle_PaidPlanUser_ReturnsConfiguredLimitsAndCurrentUsage()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var userSubscriptionId = Guid.NewGuid();

        dbContext.Users.Add(CreateUser(userId, "paid-user", "paid@example.com"));
        dbContext.Subscriptions.Add(new Subscription
        {
            Id = planId,
            Name = "Pro",
            Limits = new SubscriptionLimits
            {
                NumberOfSocialAccounts = 8,
                MaxPagesPerSocialAccount = 10,
                NumberOfWorkspaces = 5
            },
            DurationMonths = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        dbContext.UserSubscriptions.Add(new UserSubscription
        {
            Id = userSubscriptionId,
            UserId = userId,
            SubscriptionId = planId,
            Status = "Active",
            ActiveDate = DateTime.UtcNow.AddDays(-3),
            EndDate = DateTime.UtcNow.AddDays(27),
            CreatedAt = DateTime.UtcNow.AddDays(-3)
        });
        dbContext.SocialMedias.AddRange(
            new SocialMedia
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = "facebook",
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new SocialMedia
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = "facebook",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new SocialMedia
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = "instagram",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new SocialMedia
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = "threads",
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow.AddHours(-5),
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });
        dbContext.Workspaces.AddRange(
            new Workspace
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Workspace A",
                CreatedAt = DateTime.UtcNow.AddDays(-4)
            },
            new Workspace
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Workspace B",
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new Workspace
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = "Workspace Deleted",
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow.AddDays(-1),
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            });
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var handler = CreateHandler(unitOfWork);

        var result = await handler.Handle(
            new GetCurrentSubscriptionEntitlementsQuery(userId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new CurrentSubscriptionEntitlementsResponse(
            HasActivePlan: true,
            CurrentSubscriptionId: userSubscriptionId,
            CurrentPlanId: planId,
            CurrentPlanName: "Pro",
            MaxSocialAccounts: 8,
            CurrentSocialAccounts: 2,
            RemainingSocialAccounts: 6,
            MaxPagesPerSocialAccount: 10,
            CurrentWorkspaceCount: 2,
            MaxWorkspaces: 5));
    }

    private static GetCurrentSubscriptionEntitlementsQueryHandler CreateHandler(IUnitOfWork unitOfWork)
    {
        var stateService = new UserSubscriptionStateService(unitOfWork);
        var entitlementService = new UserSubscriptionEntitlementService(unitOfWork, stateService);
        return new GetCurrentSubscriptionEntitlementsQueryHandler(unitOfWork, entitlementService);
    }

    private static User CreateUser(Guid userId, string username, string email) =>
        new()
        {
            Id = userId,
            Username = username,
            PasswordHash = "hash",
            Email = email,
            FullName = username,
            CreatedAt = DateTime.UtcNow
        };

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new SubscriptionEntitlementsTestDbContext(options);
    }

    private sealed class SubscriptionEntitlementsTestDbContext : MyDbContext
    {
        public SubscriptionEntitlementsTestDbContext(DbContextOptions<MyDbContext> options)
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
}
