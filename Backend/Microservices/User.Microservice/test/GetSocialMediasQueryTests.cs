using System.Text.Json;
using Application.Abstractions.Facebook;
using Application.Abstractions.SocialMedia;
using Application.SocialMedias.Queries;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class GetSocialMediasQueryTests
{
    [Fact]
    public async Task Handle_ShouldSyncMissingFacebookPages_FromExistingUserAccessToken()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();

        dbContext.SocialMedias.Add(new SocialMedia
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = "facebook",
            Metadata = JsonDocument.Parse(
                """
                {
                  "provider": "facebook",
                  "id": "user-profile-id",
                  "name": "Facebook User",
                  "page_id": "page-1",
                  "page_name": "Page One",
                  "page_access_token": "page-token-1",
                  "access_token": "user-access-token"
                }
                """),
            CreatedAt = new DateTime(2026, 04, 08, 0, 0, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var profileService = new Mock<ISocialMediaProfileService>();
        var facebookOAuthService = new Mock<IFacebookOAuthService>();

        facebookOAuthService
            .Setup(service => service.FetchProfileAsync("user-access-token", It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(Result.Success(new FacebookProfileResponse
            {
                Id = "user-profile-id",
                Name = "Facebook User",
                Email = "user@example.com",
                Pages =
                [
                    new FacebookPageProfile("page-1", "Page One", "page-token-1", 10, 20, 30),
                    new FacebookPageProfile("page-2", "Page Two", "page-token-2", 40, 50, 60)
                ]
            }));

        profileService
            .Setup(service => service.GetUserProfileAsync("facebook", It.IsAny<JsonDocument?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, JsonDocument? metadata, CancellationToken _) =>
            {
                var root = metadata!.RootElement;
                return Result.Success(new SocialMediaUserProfile(
                    UserId: root.GetProperty("page_id").GetString(),
                    Username: null,
                    DisplayName: root.GetProperty("name").GetString(),
                    ProfilePictureUrl: null,
                    Bio: null,
                    FollowerCount: null,
                    FollowingCount: null,
                    PostCount: null,
                    PageLikeCount: null));
            });

        var handler = new GetSocialMediasQueryHandler(
            unitOfWork,
            profileService.Object,
            facebookOAuthService.Object);

        var result = await handler.Handle(
            new GetSocialMediasQuery(userId, null, null, 50),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Select(item => item.Profile?.DisplayName).Should().OnlyContain(name => name == "Facebook User");

        var storedAccounts = await dbContext.SocialMedias
            .Where(item => item.UserId == userId && item.Type == "facebook" && !item.IsDeleted)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();

        storedAccounts.Should().HaveCount(2);
        storedAccounts.Select(item => item.Metadata!.RootElement.GetProperty("page_id").GetString())
            .Should()
            .BeEquivalentTo(["page-1", "page-2"]);
        storedAccounts.Select(item => item.Metadata!.RootElement.GetProperty("name").GetString())
            .Should()
            .OnlyContain(name => name == "Facebook User");
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
