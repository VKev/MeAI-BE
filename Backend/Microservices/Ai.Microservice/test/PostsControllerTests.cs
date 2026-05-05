using System.Security.Claims;
using Application.Posts.Commands;
using Application.Posts.Models;
using Application.Posts.Queries;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SharedLibrary.Common.ResponseModel;
using WebApi.Controllers;

namespace test;

public sealed class PostsControllerTests
{
    [Fact]
    public async Task Enhance_ShouldReturnUnauthorized_WhenUserIdClaimMissing()
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var controller = new PostsController(mediator.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity())
                }
            }
        };

        var result = await controller.Enhance(Guid.NewGuid(), new EnhanceExistingPostRequest("instagram", null, null, null, null), CancellationToken.None);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorized.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Enhance_ShouldSendExpectedCommand_ToMediator()
    {
        var userId = Guid.NewGuid();
        var postId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();

        mediator
            .Setup(x => x.Send(
                It.Is<EnhanceExistingPostCommand>(command =>
                    command.UserId == userId &&
                    command.PostId == postId &&
                    command.Platform == "instagram" &&
                    command.ResourceIds != null &&
                    command.ResourceIds.Count == 1 &&
                    command.ResourceIds[0] == resourceId &&
                    command.Language == "vi" &&
                    command.Instruction == "friendly" &&
                    command.SuggestionCount == 3),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new EnhanceExistingPostResponse(
                postId,
                "ig",
                [resourceId],
                new EnhancedPostSuggestionResponse("caption", ["#a"], ["#trend"], "cta"),
                [])));

        var controller = CreateController(mediator.Object, userId);

        var result = await controller.Enhance(
            postId,
            new EnhanceExistingPostRequest("instagram", [resourceId], "vi", "friendly", 3),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        mediator.VerifyAll();
    }

    [Fact]
    public async Task Enhance_ShouldReturnProblemDetails_WhenRequestBodyMissing()
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var controller = CreateController(mediator.Object, Guid.NewGuid());

        var result = await controller.Enhance(Guid.NewGuid(), null, CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
        problem.Type.Should().Be("Post.EnhanceInvalidRequest");
    }

    [Fact]
    public async Task GetAll_ShouldMapAccountAndSocialAliases_ToPostFilters()
    {
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();

        mediator
            .Setup(x => x.Send(
                It.Is<GetUserPostsQuery>(query =>
                    query.UserId == userId &&
                    query.Status == "draft" &&
                    query.SocialMediaId == accountId &&
                    query.Platform == "facebook"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IEnumerable<PostResponse>>(Array.Empty<PostResponse>()));

        var controller = CreateController(mediator.Object, userId);

        var result = await controller.GetAll(
            cursorCreatedAt: null,
            cursorId: null,
            limit: 20,
            status: "draft",
            socialMediaId: null,
            accountId: accountId,
            platform: null,
            social: "facebook",
            cancellationToken: CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        mediator.VerifyAll();
    }

    private static PostsController CreateController(IMediator mediator, Guid userId)
    {
        return new PostsController(mediator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
                        authenticationType: "test"))
                }
            }
        };
    }
}
