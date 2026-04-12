using System.Text.Json;
using Application.Abstractions.Facebook;
using Application.Abstractions.Instagram;
using Application.Abstractions.Threads;
using Application.Abstractions.TikTok;
using FluentAssertions;
using Infrastructure.Logic.SocialMedia;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class SocialMediaProfileServiceTests
{
    [Fact]
    public async Task GetUserProfileAsync_ShouldUseFacebookPageFromMetadata_WhenMultiplePagesExist()
    {
        var tikTokOAuthService = new Mock<ITikTokOAuthService>();
        var threadsOAuthService = new Mock<IThreadsOAuthService>();
        var facebookOAuthService = new Mock<IFacebookOAuthService>();
        var instagramOAuthService = new Mock<IInstagramOAuthService>();

        facebookOAuthService
            .Setup(service => service.FetchProfileAsync("user-access-token", It.IsAny<CancellationToken>(), "page-2"))
            .ReturnsAsync(Result.Success(new FacebookProfileResponse
            {
                Id = "user-profile-id",
                Name = "Facebook User",
                PageId = "page-2",
                PageName = "Second Page",
                PageAccessToken = "page-token-2",
                PageFollowerCount = 123,
                PagePostCount = 45,
                PageLikeCount = 67
            }));

        var service = new SocialMediaProfileService(
            tikTokOAuthService.Object,
            threadsOAuthService.Object,
            facebookOAuthService.Object,
            instagramOAuthService.Object);

        using var metadata = JsonDocument.Parse(
            """
            {
              "page_id": "page-2",
              "page_name": "Second Page",
              "page_access_token": "page-token-2",
              "access_token": "user-access-token"
            }
            """);

        var result = await service.GetUserProfileAsync("facebook", metadata, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.UserId.Should().Be("page-2");
        result.Value.DisplayName.Should().Be("Facebook User");
        result.Value.FollowerCount.Should().Be(123);

        facebookOAuthService.Verify(
            oauthService => oauthService.FetchProfileAsync(
                "user-access-token",
                It.IsAny<CancellationToken>(),
                "page-2"),
            Times.Once);
    }
}
