using Application.Abstractions.Resources;
using Application.Abstractions.SocialMedias;
using Application.Posts;
using Application.Posts.Queries;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace AiMicroservice.Tests.Application.Posts.Queries;

public sealed class GetPostBuilderByIdQueryTests
{
    [Fact]
    public async Task Handle_ShouldReturnBuilderDetailsGroupedByPlatform()
    {
        var userId = Guid.NewGuid();
        var builderId = Guid.NewGuid();
        var facebookSocialMediaId = Guid.NewGuid();
        var firstResourceId = Guid.NewGuid();
        var secondResourceId = Guid.NewGuid();
        var thirdResourceId = Guid.NewGuid();

        var postBuilderRepository = new Mock<IPostBuilderRepository>();
        var userResourceService = new Mock<IUserResourceService>();
        var postPublicationRepository = new Mock<IPostPublicationRepository>();
        var userSocialMediaService = new Mock<IUserSocialMediaService>();

        var posts = new List<Post>
        {
            new()
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PostBuilderId = builderId,
                SocialMediaId = facebookSocialMediaId,
                Platform = null,
                Title = "Facebook draft",
                Content = new PostContent
                {
                    Content = "Facebook caption",
                    PostType = "posts",
                    ResourceList = [firstResourceId.ToString()]
                },
                Status = "draft",
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            },
            new()
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PostBuilderId = builderId,
                SocialMediaId = null,
                Platform = "tiktok",
                Title = "TikTok draft",
                Content = new PostContent
                {
                    Content = "TikTok caption",
                    PostType = "posts",
                    ResourceList = [secondResourceId.ToString(), thirdResourceId.ToString()]
                },
                Status = "draft",
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            }
        };

        postBuilderRepository
            .Setup(repository => repository.GetByIdAsync(builderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostBuilder
            {
                Id = builderId,
                UserId = userId,
                WorkspaceId = Guid.NewGuid(),
                PostType = "posts",
                ResourceIds = $"[\"{firstResourceId}\",\"{secondResourceId}\",\"{thirdResourceId}\"]",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3),
                Posts = posts
            });

        userResourceService
            .Setup(service => service.GetPresignedResourcesAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids =>
                    ids.Contains(firstResourceId) &&
                    ids.Contains(secondResourceId) &&
                    ids.Contains(thirdResourceId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserResourcePresignResult>>(
            [
                new UserResourcePresignResult(firstResourceId, "https://cdn.example.com/1.jpg", "image/jpeg", "image"),
                new UserResourcePresignResult(secondResourceId, "https://cdn.example.com/2.jpg", "image/jpeg", "image"),
                new UserResourcePresignResult(thirdResourceId, "https://cdn.example.com/3.mp4", "video/mp4", "video")
            ]));

        postPublicationRepository
            .Setup(repository => repository.GetByPostIdsAsync(
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PostPublication>());

        userSocialMediaService
            .Setup(service => service.GetSocialMediasAsync(
                userId,
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids[0] == facebookSocialMediaId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<UserSocialMediaResult>>(
            [
                new UserSocialMediaResult(facebookSocialMediaId, "facebook", null)
            ]));

        var postResponseBuilder = new PostResponseBuilder(
            userResourceService.Object,
            postPublicationRepository.Object);

        var handler = new GetPostBuilderByIdQueryHandler(
            postBuilderRepository.Object,
            postResponseBuilder,
            userSocialMediaService.Object);

        var result = await handler.Handle(
            new GetPostBuilderByIdQuery(builderId, userId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(builderId);
        result.Value.ResourceIds.Should().Equal(firstResourceId, secondResourceId, thirdResourceId);
        result.Value.SocialMedia.Should().HaveCount(2);
        result.Value.SocialMedia[0].Platform.Should().Be("facebook");
        result.Value.SocialMedia[0].Posts.Should().ContainSingle();
        result.Value.SocialMedia[1].Platform.Should().Be("tiktok");
        result.Value.SocialMedia[1].Posts.Should().ContainSingle();
    }
}
