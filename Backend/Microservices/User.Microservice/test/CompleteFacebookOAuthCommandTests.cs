using System.Text.Json;
using Application.Abstractions.Facebook;
using Application.Abstractions.Meta;
using Application.Abstractions.SocialMedia;
using Application.SocialMedias.Commands;
using Application.Subscriptions.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class CompleteFacebookOAuthCommandTests
{
    [Fact]
    public async Task Handle_ShouldCreateOneSocialMediaPerFacebookPage()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();

        dbContext.Users.Add(new User
        {
            Id = userId,
            Username = "facebook-user",
            PasswordHash = "hash",
            Email = "user@example.com",
            FullName = "Existing User",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var facebookOAuthService = new Mock<IFacebookOAuthService>();
        var entitlementService = new Mock<IUserSubscriptionEntitlementService>();
        var profileService = new Mock<ISocialMediaProfileService>();
        var publishEndpoint = new Mock<IPublishEndpoint>();

        var state = userId;
        facebookOAuthService
            .Setup(service => service.TryValidateState("valid-state", out state))
            .Returns(true);
        facebookOAuthService
            .Setup(service => service.ExchangeCodeForTokenAsync("valid-code", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FacebookAccessTokenResponse
            {
                AccessToken = "user-access-token",
                ExpiresIn = 3600
            }));
        facebookOAuthService
            .Setup(service => service.ValidateTokenAsync("user-access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new MetaDebugToken
            {
                IsValid = true,
                AppId = "test-app"
            }));
        facebookOAuthService
            .Setup(service => service.FetchProfileAsync("user-access-token", It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(Result.Success(new FacebookProfileResponse
            {
                Id = "user-profile-id",
                Name = "Facebook User",
                Email = "facebook@example.com",
                Pages =
                [
                    new FacebookPageProfile("page-1", "Page One", "page-token-1", 12, 34, 56),
                    new FacebookPageProfile("page-2", "Page Two", "page-token-2", 78, 90, 12)
                ]
            }));

        entitlementService
            .Setup(service => service.GetCurrentEntitlementAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserSubscriptionEntitlement(
                new UserSubscription
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SubscriptionId = Guid.NewGuid(),
                    Status = "active"
                },
                new Subscription
                {
                    Id = Guid.NewGuid(),
                    Name = "Pro",
                    Limits = new SubscriptionLimits
                    {
                        NumberOfSocialAccounts = 10,
                        NumberOfWorkspaces = 3
                    }
                }));

        profileService
            .Setup(service => service.GetUserProfileAsync("facebook", It.IsAny<JsonDocument?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SocialMediaUserProfile(
                UserId: "page-1",
                Username: null,
                DisplayName: "Page One",
                ProfilePictureUrl: null,
                Bio: null,
                FollowerCount: 34,
                FollowingCount: null,
                PostCount: 56,
                PageLikeCount: 12)));

        var handler = new CompleteFacebookOAuthCommandHandler(
            unitOfWork,
            facebookOAuthService.Object,
            entitlementService.Object,
            profileService.Object,
            publishEndpoint.Object,
            NullLogger<CompleteFacebookOAuthCommandHandler>.Instance);

        var result = await handler.Handle(
            new CompleteFacebookOAuthCommand("valid-code", "valid-state", null, null),
            CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Type.Should().Be("facebook");

        var socialMedias = await dbContext.SocialMedias
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();

        socialMedias.Should().HaveCount(2);
        socialMedias.Select(item => item.Metadata!.RootElement.GetProperty("page_id").GetString())
            .Should()
            .BeEquivalentTo(["page-1", "page-2"]);
        socialMedias.Select(item => item.Metadata!.RootElement.GetProperty("page_name").GetString())
            .Should()
            .BeEquivalentTo(["Page One", "Page Two"]);
        socialMedias.Select(item => item.Metadata!.RootElement.GetProperty("page_access_token").GetString())
            .Should()
            .BeEquivalentTo(["page-token-1", "page-token-2"]);

        entitlementService.Verify(
            service => service.GetCurrentEntitlementAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new SocialMediaTestDbContext(options);
    }

    private sealed class SocialMediaTestDbContext : MyDbContext
    {
        public SocialMediaTestDbContext(DbContextOptions<MyDbContext> options)
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
