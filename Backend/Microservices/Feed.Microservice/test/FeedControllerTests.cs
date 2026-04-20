using System.Security.Claims;
using Application.Common;
using Application.Follows.Models;
using Application.Follows.Queries;
using Application.Posts.Commands;
using Application.Posts.Models;
using Application.Posts.Queries;
using Application.Profiles.Models;
using Application.Profiles.Queries;
using Application.Reports.Commands;
using Application.Reports.Models;
using Application.Reports.Queries;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SharedLibrary.Common.ResponseModel;
using WebApi.Controllers;

namespace test;

public sealed class FeedControllerTests
{
    [Fact]
    public async Task GetPublicProfileByUsername_Should_SendUsernameQuery_AndReturnOk()
    {
        var expectedProfile = new PublicProfileResponse(
            Guid.NewGuid(),
            "alice",
            "Alice Nguyen",
            "https://cdn.example.com/alice.jpg",
            10,
            12);

        var mediator = new Mock<IMediator>();
        var expectedResult = Result.Success(expectedProfile);

        mediator
            .Setup(item => item.Send(It.IsAny<GetPublicProfileByUsernameQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var controller = CreateController(mediator.Object);

        var actionResult = await controller.GetPublicProfileByUsername("alice", CancellationToken.None);

        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeSameAs(expectedResult);
        mediator.Verify(
            item => item.Send(
                It.Is<GetPublicProfileByUsernameQuery>(query => query == new GetPublicProfileByUsernameQuery("alice")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPostsByUsername_Should_SendNullRequestingUser_WhenAnonymous()
    {
        IReadOnlyList<PostResponse> expectedPosts = new List<PostResponse>
        {
            CreatePostResponse()
        };

        var mediator = new Mock<IMediator>();
        var cursorCreatedAt = new DateTime(2026, 4, 19, 8, 30, 0, DateTimeKind.Utc);
        var cursorId = Guid.NewGuid();
        var expectedResult = Result.Success(expectedPosts);

        mediator
            .Setup(item => item.Send(It.IsAny<GetPostsByUsernameQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var controller = CreateController(mediator.Object);

        var actionResult = await controller.GetPostsByUsername(
            "alice",
            cursorCreatedAt,
            cursorId,
            15,
            CancellationToken.None);

        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeSameAs(expectedResult);
        mediator.Verify(
            item => item.Send(
                It.Is<GetPostsByUsernameQuery>(query => query == new GetPostsByUsernameQuery("alice", cursorCreatedAt, cursorId, 15, null)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPostsByUsername_Should_IncludeAuthenticatedUserId_WhenAvailable()
    {
        IReadOnlyList<PostResponse> expectedPosts = new List<PostResponse>
        {
            CreatePostResponse()
        };

        var viewerId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        var expectedResult = Result.Success(expectedPosts);

        mediator
            .Setup(item => item.Send(It.IsAny<GetPostsByUsernameQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var controller = CreateController(mediator.Object, viewerId);

        await controller.GetPostsByUsername("alice", null, null, 20, CancellationToken.None);

        mediator.Verify(
            item => item.Send(
                It.Is<GetPostsByUsernameQuery>(query => query == new GetPostsByUsernameQuery("alice", null, null, 20, viewerId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetFollowSuggestions_Should_SendAuthenticatedUserIdAndLimit()
    {
        IReadOnlyList<FollowSuggestionResponse> expectedSuggestions = new List<FollowSuggestionResponse>
        {
            CreateFollowSuggestionResponse()
        };

        var currentUserId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        var expectedResult = Result.Success(expectedSuggestions);

        mediator
            .Setup(item => item.Send(It.IsAny<GetFollowSuggestionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var controller = CreateController(mediator.Object, currentUserId);

        var actionResult = await controller.GetFollowSuggestions(12, CancellationToken.None);

        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeSameAs(expectedResult);
        mediator.Verify(
            item => item.Send(
                It.Is<GetFollowSuggestionsQuery>(query => query == new GetFollowSuggestionsQuery(currentUserId, 12)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetFollowSuggestions_Should_ReturnUnauthorized_WhenUserMissing()
    {
        var mediator = new Mock<IMediator>();
        var controller = CreateController(mediator.Object);

        var actionResult = await controller.GetFollowSuggestions(10, CancellationToken.None);

        var unauthorizedResult = actionResult.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorizedResult.Value.Should().BeEquivalentTo(new { Message = "Unauthorized" });
        mediator.Verify(
            item => item.Send(It.IsAny<GetFollowSuggestionsQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAdminReports_Should_ForwardStatusAndTargetTypeFilters()
    {
        IReadOnlyList<ReportResponse> expectedReports = new List<ReportResponse>
        {
            CreateReportResponse()
        };

        var adminUserId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        var expectedResult = Result.Success(expectedReports);

        mediator
            .Setup(item => item.Send(It.IsAny<GetAdminReportsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var controller = CreateController(mediator.Object, adminUserId);

        var actionResult = await controller.GetAdminReports("InReview", "Post", CancellationToken.None);

        actionResult.Should().BeOfType<OkObjectResult>();
        mediator.Verify(
            item => item.Send(
                It.Is<GetAdminReportsQuery>(query => query == new GetAdminReportsQuery("InReview", "Post")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReviewReport_Should_SendAdminUserIdAndRequestPayload()
    {
        var adminUserId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var request = new ReviewReportRequest("Resolved", "DeleteTargetPost", "Removed violating post");
        var expectedResponse = CreateReportResponse(reportId);

        var mediator = new Mock<IMediator>();
        var expectedResult = Result.Success(expectedResponse);

        mediator
            .Setup(item => item.Send(It.IsAny<ReviewReportCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var controller = CreateController(mediator.Object, adminUserId);

        var actionResult = await controller.ReviewReport(reportId, request, CancellationToken.None);

        actionResult.Should().BeOfType<OkObjectResult>();
        mediator.Verify(
            item => item.Send(
                It.Is<ReviewReportCommand>(command => command == new ReviewReportCommand(adminUserId, reportId, "Resolved", "DeleteTargetPost", "Removed violating post")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdatePost_Should_SendAuthenticatedUserIdAndPayload()
    {
        var currentUserId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var request = new UpdatePostRequest(
            "updated content #feed",
            new[] { Guid.NewGuid() },
            "image/jpeg");
        var expectedPost = CreatePostResponse(postId, currentUserId, request.Content, request.MediaType);

        var mediator = new Mock<IMediator>();
        var expectedResult = Result.Success(expectedPost);

        mediator
            .Setup(item => item.Send(It.IsAny<UpdatePostCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var controller = CreateController(mediator.Object, currentUserId);

        var actionResult = await controller.UpdatePost(postId, request, CancellationToken.None);

        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().BeSameAs(expectedResult);
        mediator.Verify(
            item => item.Send(
                It.Is<UpdatePostCommand>(command =>
                    command == new UpdatePostCommand(currentUserId, postId, request.Content, request.ResourceIds, request.MediaType)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdatePost_Should_ReturnUnauthorized_WhenUserMissing()
    {
        var mediator = new Mock<IMediator>();
        var controller = CreateController(mediator.Object);

        var actionResult = await controller.UpdatePost(
            Guid.NewGuid(),
            new UpdatePostRequest("updated content", null, null),
            CancellationToken.None);

        var unauthorizedResult = actionResult.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorizedResult.Value.Should().BeEquivalentTo(new MessageResponse("Unauthorized"));
        mediator.Verify(
            item => item.Send(It.IsAny<UpdatePostCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static FeedController CreateController(IMediator mediator, Guid? userId = null)
    {
        var controller = new FeedController(mediator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        if (userId.HasValue)
        {
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) },
                    authenticationType: "Test"));
        }

        return controller;
    }

    private static PostResponse CreatePostResponse(
        Guid? postId = null,
        Guid? userId = null,
        string? content = "post content",
        string? mediaType = "image/jpeg")
    {
        return new PostResponse(
            postId ?? Guid.NewGuid(),
            userId ?? Guid.NewGuid(),
            "tester",
            "https://cdn.example.com/avatar.jpg",
            content,
            "https://cdn.example.com/post.jpg",
            mediaType,
            new List<PostMediaResponse>(),
            4,
            2,
            new List<string> { "feed", "moderation" },
            DateTime.UtcNow,
            DateTime.UtcNow,
            null,
            null);
    }

    private static FollowSuggestionResponse CreateFollowSuggestionResponse()
    {
        return new FollowSuggestionResponse(
            Guid.NewGuid(),
            "alice",
            "Alice Nguyen",
            "https://cdn.example.com/alice.jpg",
            7);
    }

    private static ReportResponse CreateReportResponse(Guid? reportId = null)
    {
        return new ReportResponse(
            reportId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            "Post",
            Guid.NewGuid(),
            "Spam content",
            "Pending",
            null,
            null,
            null,
            "None",
            DateTime.UtcNow,
            DateTime.UtcNow);
    }
}
