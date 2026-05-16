using System.Text.Json;
using Application.Abstractions.Data;
using Application.Abstractions.Facebook;
using Application.Abstractions.Instagram;
using Application.Abstractions.Threads;
using Application.Abstractions.TikTok;
using Application.Abstractions.SocialMedia;
using Application.SocialMedias.Commands;
using Application.Subscriptions.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class SocialOAuthLimitEnforcementTests
{
    [Theory]
    [InlineData("facebook")]
    [InlineData("instagram")]
    [InlineData("tiktok")]
    [InlineData("threads")]
    public async Task InitiateOAuth_WhenUserHasReachedSocialAccountLimit_ReturnsLimitExceeded(string provider)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();

        dbContext.Users.Add(CreateUser(userId));
        dbContext.SocialMedias.AddRange(
            CreateSocialMedia(userId, "facebook"),
            CreateSocialMedia(userId, "instagram"));
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var stateService = new UserSubscriptionStateService(unitOfWork);
        var entitlementService = new UserSubscriptionEntitlementService(unitOfWork, stateService);

        SharedLibrary.Common.ResponseModel.Result result = provider switch
        {
            "facebook" => await HandleFacebookInitiateAsync(userId, unitOfWork, entitlementService),
            "instagram" => await HandleInstagramInitiateAsync(userId, unitOfWork, entitlementService),
            "tiktok" => await HandleTikTokInitiateAsync(userId, unitOfWork, entitlementService),
            "threads" => await HandleThreadsInitiateAsync(userId, unitOfWork, entitlementService),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SocialMedia.LimitExceeded");
        result.Error.Description.Should().Contain("allows up to 2 linked social account");
    }

    [Fact]
    public async Task CompleteFacebookOAuth_WhenRacePushesUserOverLimit_ReturnsLimitExceeded()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();

        dbContext.Users.Add(CreateUser(userId));
        dbContext.SocialMedias.AddRange(
            CreateSocialMedia(userId, "instagram"),
            CreateSocialMedia(userId, "tiktok"));
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var facebookOAuthService = new Mock<IFacebookOAuthService>();
        var profileService = new Mock<ISocialMediaProfileService>();
        var publishEndpoint = new Mock<IPublishEndpoint>();
        var stateService = new UserSubscriptionStateService(unitOfWork);
        var entitlementService = new UserSubscriptionEntitlementService(unitOfWork, stateService);

        var state = userId;
        facebookOAuthService
            .Setup(service => service.TryValidateState("valid-state", out state))
            .Returns(true);
        facebookOAuthService
            .Setup(service => service.ExchangeCodeForTokenAsync("valid-code", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SharedLibrary.Common.ResponseModel.Result.Success(new FacebookAccessTokenResponse
            {
                AccessToken = "user-access-token",
                ExpiresIn = 3600
            }));
        facebookOAuthService
            .Setup(service => service.ValidateTokenAsync("user-access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SharedLibrary.Common.ResponseModel.Result.Success(new Application.Abstractions.Meta.MetaDebugToken
            {
                IsValid = true,
                AppId = "test-app"
            }));
        facebookOAuthService
            .Setup(service => service.FetchProfileAsync("user-access-token", It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(SharedLibrary.Common.ResponseModel.Result.Success(new FacebookProfileResponse
            {
                Id = "facebook-user",
                Name = "Facebook User",
                Email = "facebook@example.com",
                Pages = [new FacebookPageProfile("page-1", "Page One", "page-token-1", 1, 2, 3)]
            }));

        var handler = new CompleteFacebookOAuthCommandHandler(
            unitOfWork,
            facebookOAuthService.Object,
            entitlementService,
            profileService.Object,
            publishEndpoint.Object,
            NullLogger<CompleteFacebookOAuthCommandHandler>.Instance);

        var result = await handler.Handle(
            new CompleteFacebookOAuthCommand("valid-code", "valid-state", null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SocialMedia.LimitExceeded");
        result.Error.Description.Should().Contain("allows up to 2 linked social account");
    }

    private static async Task<Result> HandleFacebookInitiateAsync(
        Guid userId,
        IUnitOfWork unitOfWork,
        IUserSubscriptionEntitlementService entitlementService)
    {
        return await CreateFacebookHandler(unitOfWork, entitlementService).Handle(
            new InitiateFacebookOAuthCommand(userId, null),
            CancellationToken.None);
    }

    private static async Task<Result> HandleInstagramInitiateAsync(
        Guid userId,
        IUnitOfWork unitOfWork,
        IUserSubscriptionEntitlementService entitlementService)
    {
        return await CreateInstagramHandler(unitOfWork, entitlementService).Handle(
            new InitiateInstagramOAuthCommand(userId, null),
            CancellationToken.None);
    }

    private static async Task<Result> HandleTikTokInitiateAsync(
        Guid userId,
        IUnitOfWork unitOfWork,
        IUserSubscriptionEntitlementService entitlementService)
    {
        return await CreateTikTokHandler(unitOfWork, entitlementService).Handle(
            new InitiateTikTokOAuthCommand(userId, null),
            CancellationToken.None);
    }

    private static async Task<Result> HandleThreadsInitiateAsync(
        Guid userId,
        IUnitOfWork unitOfWork,
        IUserSubscriptionEntitlementService entitlementService)
    {
        return await CreateThreadsHandler(unitOfWork, entitlementService).Handle(
            new InitiateThreadsOAuthCommand(userId, null),
            CancellationToken.None);
    }

    private static InitiateFacebookOAuthCommandHandler CreateFacebookHandler(
        IUnitOfWork unitOfWork,
        IUserSubscriptionEntitlementService entitlementService)
    {
        var oauthService = new Mock<IFacebookOAuthService>();
        oauthService
            .Setup(service => service.GenerateAuthorizationUrl(It.IsAny<Guid>(), It.IsAny<string?>()))
            .Returns((Guid userId, string? _) => ($"https://facebook.test/{userId}", "state"));

        return new InitiateFacebookOAuthCommandHandler(oauthService.Object, entitlementService, unitOfWork);
    }

    private static InitiateInstagramOAuthCommandHandler CreateInstagramHandler(
        IUnitOfWork unitOfWork,
        IUserSubscriptionEntitlementService entitlementService)
    {
        var oauthService = new Mock<IInstagramOAuthService>();
        oauthService
            .Setup(service => service.GenerateAuthorizationUrl(It.IsAny<Guid>(), It.IsAny<string?>()))
            .Returns((Guid userId, string? _) => ($"https://instagram.test/{userId}", "state"));

        return new InitiateInstagramOAuthCommandHandler(oauthService.Object, entitlementService, unitOfWork);
    }

    private static InitiateTikTokOAuthCommandHandler CreateTikTokHandler(
        IUnitOfWork unitOfWork,
        IUserSubscriptionEntitlementService entitlementService)
    {
        var oauthService = new Mock<ITikTokOAuthService>();
        oauthService
            .Setup(service => service.GenerateAuthorizationUrl(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns((Guid userId, string _) => ($"https://tiktok.test/{userId}", "state", "verifier"));

        return new InitiateTikTokOAuthCommandHandler(
            oauthService.Object,
            new MemoryCache(new MemoryCacheOptions()),
            entitlementService,
            unitOfWork);
    }

    private static InitiateThreadsOAuthCommandHandler CreateThreadsHandler(
        IUnitOfWork unitOfWork,
        IUserSubscriptionEntitlementService entitlementService)
    {
        var oauthService = new Mock<IThreadsOAuthService>();
        oauthService
            .Setup(service => service.GenerateAuthorizationUrl(It.IsAny<Guid>(), It.IsAny<string?>()))
            .Returns((Guid userId, string? _) => ($"https://threads.test/{userId}", "state"));

        return new InitiateThreadsOAuthCommandHandler(oauthService.Object, entitlementService, unitOfWork);
    }

    private static User CreateUser(Guid userId) =>
        new()
        {
            Id = userId,
            Username = $"user-{userId:N}",
            PasswordHash = "hash",
            Email = $"{userId:N}@example.com",
            FullName = "Test User",
            CreatedAt = DateTime.UtcNow
        };

    private static SocialMedia CreateSocialMedia(Guid userId, string type) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            CreatedAt = DateTime.UtcNow
        };

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new SocialOAuthTestDbContext(options);
    }

    private sealed class SocialOAuthTestDbContext : MyDbContext
    {
        public SocialOAuthTestDbContext(DbContextOptions<MyDbContext> options)
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
