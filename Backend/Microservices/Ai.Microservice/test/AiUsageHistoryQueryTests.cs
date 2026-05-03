using Application.Admin.Queries;
using Application.Usage;
using Application.Usage.Models;
using Application.Usage.Queries;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;
using SharedLibrary.Common.ResponseModel;

namespace test;

public sealed class AiUsageHistoryQueryTests
{
    [Fact]
    public async Task GetMyAiUsageHistory_ShouldReturnOnlyCurrentUserRecordsWithinRange()
    {
        var userId = Guid.NewGuid();
        var expectedPage = new AiSpendRecordHistoryPage(
            new List<AiSpendRecord>
            {
                CreateRecord(userId, createdAt: new DateTime(2026, 05, 03, 10, 0, 0, DateTimeKind.Utc))
            },
            new DateTime(2026, 05, 03, 10, 0, 0, DateTimeKind.Utc),
            Guid.NewGuid());

        var repository = new Mock<IAiSpendRecordRepository>();
        repository
            .Setup(x => x.GetHistoryAsync(
                It.Is<AiSpendRecordHistoryQuery>(query =>
                    query.UserId == userId &&
                    query.FromUtc == new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc) &&
                    query.ToUtc == new DateTime(2026, 05, 04, 0, 0, 0, DateTimeKind.Utc) &&
                    query.Limit == 20),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPage);

        var timingResolver = CreateTimingResolver();
        var handler = new GetMyAiUsageHistoryQueryHandler(repository.Object, timingResolver.Object);

        var result = await handler.Handle(
            new GetMyAiUsageHistoryQuery(
                userId,
                new AiUsageHistoryFilter(
                    FromUtc: new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc),
                    ToUtc: new DateTime(2026, 05, 04, 0, 0, 0, DateTimeKind.Utc),
                    ActionType: null,
                    Status: null,
                    WorkspaceId: null,
                    Provider: null,
                    Model: null,
                    ReferenceType: null,
                    CursorCreatedAt: null,
                    CursorId: null,
                    Limit: null)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].UserId.Should().Be(userId);
        repository.VerifyAll();
    }

    [Fact]
    public async Task GetAdminAiUsageHistory_ShouldSupportAdminUserIdAndFilters()
    {
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var cursorCreatedAt = new DateTime(2026, 05, 03, 12, 0, 0, DateTimeKind.Utc);
        var cursorId = Guid.NewGuid();

        var expectedPage = new AiSpendRecordHistoryPage(
            new List<AiSpendRecord>
            {
                CreateRecord(userId, workspaceId, createdAt: cursorCreatedAt.AddMinutes(-1), actionType: "video_generation", status: "debited")
            },
            cursorCreatedAt.AddMinutes(-1),
            Guid.NewGuid());

        var repository = new Mock<IAiSpendRecordRepository>();
        repository
            .Setup(x => x.GetHistoryAsync(
                It.Is<AiSpendRecordHistoryQuery>(query =>
                    query.UserId == userId &&
                    query.ActionType == "video_generation" &&
                    query.Status == "DEBITED" &&
                    query.WorkspaceId == workspaceId &&
                    query.CursorCreatedAt == cursorCreatedAt &&
                    query.CursorId == cursorId &&
                    query.Limit == 50),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPage);

        var timingResolver = CreateTimingResolver();
        var handler = new GetAdminAiUsageHistoryQueryHandler(repository.Object, timingResolver.Object);

        var result = await handler.Handle(
            new GetAdminAiUsageHistoryQuery(
                new AiUsageHistoryFilter(
                    FromUtc: null,
                    ToUtc: null,
                    ActionType: "video_generation",
                    Status: "DEBITED",
                    WorkspaceId: workspaceId,
                    Provider: null,
                    Model: null,
                    ReferenceType: null,
                    CursorCreatedAt: cursorCreatedAt,
                    CursorId: cursorId,
                    Limit: 50,
                    UserId: userId)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].WorkspaceId.Should().Be(workspaceId);
        repository.VerifyAll();
    }

    [Fact]
    public async Task GetAdminAiUsageHistory_ShouldReturnValidationError_WhenDateRangeInvalid()
    {
        var repository = new Mock<IAiSpendRecordRepository>(MockBehavior.Strict);
        var timingResolver = CreateTimingResolver();
        var handler = new GetAdminAiUsageHistoryQueryHandler(repository.Object, timingResolver.Object);

        var result = await handler.Handle(
            new GetAdminAiUsageHistoryQuery(
                new AiUsageHistoryFilter(
                    FromUtc: new DateTime(2026, 05, 03, 0, 0, 0, DateTimeKind.Utc),
                    ToUtc: new DateTime(2026, 05, 03, 0, 0, 0, DateTimeKind.Utc),
                    ActionType: null,
                    Status: null,
                    WorkspaceId: null,
                    Provider: null,
                    Model: null,
                    ReferenceType: null,
                    CursorCreatedAt: null,
                    CursorId: null,
                    Limit: 20)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Should().BeAssignableTo<IValidationResult>();
        ((IValidationResult)result).Errors.Should().ContainSingle(error => error.Code == "AiUsageHistory.InvalidDateRange");
    }

    [Fact]
    public async Task GetMyAiUsageHistory_ShouldReturnValidationError_WhenCursorCreatedAtMissingPartnerId()
    {
        var repository = new Mock<IAiSpendRecordRepository>(MockBehavior.Strict);
        var timingResolver = CreateTimingResolver();
        var handler = new GetMyAiUsageHistoryQueryHandler(repository.Object, timingResolver.Object);

        var result = await handler.Handle(
            new GetMyAiUsageHistoryQuery(
                Guid.NewGuid(),
                new AiUsageHistoryFilter(
                    FromUtc: null,
                    ToUtc: null,
                    ActionType: null,
                    Status: null,
                    WorkspaceId: null,
                    Provider: null,
                    Model: null,
                    ReferenceType: null,
                    CursorCreatedAt: new DateTime(2026, 05, 03, 0, 0, 0, DateTimeKind.Utc),
                    CursorId: null,
                    Limit: 20)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Should().BeAssignableTo<IValidationResult>();
        ((IValidationResult)result).Errors.Should().ContainSingle(error => error.Code == "AiUsageHistory.InvalidCursorId");
    }

    [Fact]
    public async Task GetMyAiUsageHistory_ShouldPreserveStableOrderingAndCursorFromPage()
    {
        var userId = Guid.NewGuid();
        var sameTimestamp = new DateTime(2026, 05, 03, 10, 0, 0, DateTimeKind.Utc);
        var firstId = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
        var secondId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

        var expectedPage = new AiSpendRecordHistoryPage(
            new List<AiSpendRecord>
            {
                CreateRecord(userId, id: firstId, createdAt: sameTimestamp),
                CreateRecord(userId, id: secondId, createdAt: sameTimestamp)
            },
            sameTimestamp,
            secondId);

        var repository = new Mock<IAiSpendRecordRepository>();
        repository
            .Setup(x => x.GetHistoryAsync(It.IsAny<AiSpendRecordHistoryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPage);

        var timingResolver = CreateTimingResolver();
        var handler = new GetMyAiUsageHistoryQueryHandler(repository.Object, timingResolver.Object);

        var result = await handler.Handle(
            new GetMyAiUsageHistoryQuery(
                userId,
                new AiUsageHistoryFilter(null, null, null, null, null, null, null, null, null, null, 2)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Select(x => x.SpendRecordId).Should().ContainInOrder(firstId, secondId);
        result.Value.NextCursorCreatedAt.Should().Be(sameTimestamp);
        result.Value.NextCursorId.Should().Be(secondId);
    }

    private static Mock<IAiUsageTimingResolver> CreateTimingResolver()
    {
        var timingResolver = new Mock<IAiUsageTimingResolver>();
        timingResolver
            .Setup(x => x.ResolveAsync(It.IsAny<IReadOnlyList<AiSpendRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AiUsageTiming>());

        return timingResolver;
    }

    private static AiSpendRecord CreateRecord(
        Guid userId,
        Guid? workspaceId = null,
        Guid? id = null,
        DateTime? createdAt = null,
        string actionType = "image_generation",
        string status = "debited") =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            UserId = userId,
            WorkspaceId = workspaceId,
            Provider = "kie",
            ActionType = actionType,
            Model = "nano-banana-pro",
            Variant = "1K",
            Unit = "per_image",
            Quantity = 1,
            UnitCostCoins = 4,
            TotalCoins = 4,
            Status = status,
            ReferenceType = "chat_image",
            ReferenceId = Guid.NewGuid().ToString(),
            CreatedAt = createdAt ?? new DateTime(2026, 05, 03, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = createdAt ?? new DateTime(2026, 05, 03, 10, 1, 0, DateTimeKind.Utc)
        };
}
