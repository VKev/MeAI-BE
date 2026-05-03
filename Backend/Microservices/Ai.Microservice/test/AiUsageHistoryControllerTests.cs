using System.Security.Claims;
using Application.Admin.Queries;
using Application.Usage.Models;
using Application.Usage.Queries;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SharedLibrary.Common.ResponseModel;
using WebApi.Controllers;

namespace test;

public sealed class AiUsageHistoryControllerTests
{
    [Fact]
    public async Task UserHistory_ShouldReturnUnauthorized_WhenUserIdClaimMissing()
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var controller = new AiUsageController(mediator.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity())
                }
            }
        };

        var result = await controller.GetHistory(
            new AiUsageHistoryQueryParameters(null, null, null, null, null, null, null, null, null, null, null),
            CancellationToken.None);

        var unauthorized = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
        unauthorized.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task UserHistory_ShouldReturnProblemDetails_WhenQueryParametersInvalid()
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var controller = CreateUserController(mediator.Object, Guid.NewGuid());

        var result = await controller.GetHistory(
            new AiUsageHistoryQueryParameters(null, null, null, null, null, null, null, null, null, "not-a-guid", "20"),
            CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UserHistory_ShouldSendCurrentUserIdToMediator()
    {
        var userId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(x => x.Send(
                It.Is<GetMyAiUsageHistoryQuery>(query => query.UserId == userId && query.Filter.ActionType == "image_generation"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AiUsageHistoryResponse(Array.Empty<AiUsageHistoryItemResponse>(), null, null)));

        var controller = CreateUserController(mediator.Object, userId);

        var result = await controller.GetHistory(
            new AiUsageHistoryQueryParameters(null, null, "image_generation", null, null, null, null, null, null, null, null),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        mediator.VerifyAll();
    }

    [Fact]
    public async Task AdminHistory_ShouldReturnProblemDetails_WhenMediatorReturnsValidationFailure()
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(x => x.Send(It.IsAny<GetAdminAiUsageHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidationResult<AiUsageHistoryResponse>.WithErrors(
                new[] { new Error("AiUsageHistory.InvalidLimit", "limit must be greater than 0.") }));

        var controller = new AdminAiSpendingController(mediator.Object);

        var result = await controller.GetHistory(
            new AiUsageHistoryQueryParameters(null, null, null, null, null, null, null, null, null, null, "0", Guid.NewGuid().ToString()),
            CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task AdminHistory_ShouldPassUserIdFilterToMediator()
    {
        var filterUserId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(x => x.Send(
                It.Is<GetAdminAiUsageHistoryQuery>(query => query.Filter.UserId == filterUserId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AiUsageHistoryResponse(Array.Empty<AiUsageHistoryItemResponse>(), null, null)));

        var controller = new AdminAiSpendingController(mediator.Object);

        var result = await controller.GetHistory(
            new AiUsageHistoryQueryParameters(null, null, null, null, null, null, null, null, null, null, null, filterUserId.ToString()),
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        mediator.VerifyAll();
    }

    private static AiUsageController CreateUserController(IMediator mediator, Guid userId)
    {
        return new AiUsageController(mediator)
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
