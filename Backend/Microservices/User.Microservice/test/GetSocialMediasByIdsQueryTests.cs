using System.Text.Json;
using Application.Abstractions.TikTok;
using Application.SocialMedias.Queries;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace test;

public sealed class GetSocialMediasByIdsQueryTests
{
    [Fact]
    public async Task Handle_ShouldRefreshExpiredTikTokToken_BeforeReturningMetadata()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var socialMediaId = Guid.NewGuid();

        dbContext.SocialMedias.Add(new SocialMedia
        {
            Id = socialMediaId,
            UserId = userId,
            Type = "tiktok",
            Metadata = JsonDocument.Parse(
                """
                {
                  "open_id": "old-open-id",
                  "access_token": "old-access-token",
                  "refresh_token": "refresh-token",
                  "expires_at": "2026-03-23T00:00:00.0000000Z",
                  "refresh_expires_at": "2026-04-23T00:00:00.0000000Z",
                  "display_name": "Old Name"
                }
                """),
            CreatedAt = new DateTime(2026, 03, 20, 0, 0, 0, DateTimeKind.Utc)
        });
        await dbContext.SaveChangesAsync();

        using var unitOfWork = new UnitOfWork(dbContext);
        var tikTokOAuthService = new Mock<ITikTokOAuthService>();
        tikTokOAuthService
            .Setup(service => service.RefreshTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SharedLibrary.Common.ResponseModel.Result.Success(new TikTokTokenResponse
            {
                OpenId = "new-open-id",
                AccessToken = "new-access-token",
                RefreshToken = "new-refresh-token",
                ExpiresIn = 7200,
                RefreshExpiresIn = 86400,
                Scope = "user.info.basic,video.publish",
                TokenType = "Bearer"
            }));
        tikTokOAuthService
            .Setup(service => service.GetUserProfileAsync("new-access-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(SharedLibrary.Common.ResponseModel.Result.Success(new TikTokUserProfile
            {
                OpenId = "new-open-id",
                DisplayName = "New Name",
                UnionId = "new-union-id"
            }));

        var handler = new GetSocialMediasByIdsQueryHandler(unitOfWork, tikTokOAuthService.Object);

        var result = await handler.Handle(new GetSocialMediasByIdsQuery(userId, new[] { socialMediaId }), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();

        using var returnedMetadata = JsonDocument.Parse(result.Value[0].MetadataJson!);
        returnedMetadata.RootElement.GetProperty("access_token").GetString().Should().Be("new-access-token");
        returnedMetadata.RootElement.GetProperty("refresh_token").GetString().Should().Be("new-refresh-token");
        returnedMetadata.RootElement.GetProperty("open_id").GetString().Should().Be("new-open-id");
        returnedMetadata.RootElement.GetProperty("display_name").GetString().Should().Be("New Name");

        var storedSocialMedia = await dbContext.SocialMedias.SingleAsync(item => item.Id == socialMediaId);
        storedSocialMedia.UpdatedAt.Should().NotBeNull();
        storedSocialMedia.Metadata!.RootElement.GetProperty("access_token").GetString().Should().Be("new-access-token");

        tikTokOAuthService.VerifyAll();
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
