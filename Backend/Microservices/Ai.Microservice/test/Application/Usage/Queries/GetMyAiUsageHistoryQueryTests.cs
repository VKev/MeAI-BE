using Application.Usage.Models;
using Application.Usage.Queries;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;

namespace AiMicroservice.Tests.Application.Usage.Queries;

public sealed class GetMyAiUsageHistoryQueryTests
{
    [Fact]
    public async Task Handle_ShouldEnrichResponseItemsWithTimingFields()
    {
        var spendRecord = new AiSpendRecord
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            Provider = "kie",
            ActionType = "image_generation",
            Model = "nano-banana-pro",
            Variant = null,
            Unit = "request",
            Quantity = 1,
            UnitCostCoins = 10,
            TotalCoins = 10,
            Status = "debited",
            ReferenceType = "chat_image",
            ReferenceId = Guid.NewGuid().ToString(),
            CreatedAt = new DateTime(2026, 5, 3, 14, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 5, 3, 14, 1, 0, DateTimeKind.Utc)
        };

        var page = new AiSpendRecordHistoryPage(
            [spendRecord],
            spendRecord.CreatedAt,
            spendRecord.Id);
        var timing = new AiUsageTiming(
            new DateTime(2026, 5, 3, 13, 59, 0, DateTimeKind.Utc),
            new DateTime(2026, 5, 3, 14, 0, 30, DateTimeKind.Utc),
            90);

        var repository = new Mock<IAiSpendRecordRepository>(MockBehavior.Strict);
        repository
            .Setup(repo => repo.GetHistoryAsync(
                It.Is<AiSpendRecordHistoryQuery>(query => query.UserId == spendRecord.UserId && query.Limit == 20),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var resolver = new Mock<IAiUsageTimingResolver>(MockBehavior.Strict);
        resolver
            .Setup(service => service.ResolveAsync(
                It.Is<IReadOnlyList<AiSpendRecord>>(records => records.Count == 1 && records[0].Id == spendRecord.Id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AiUsageTiming>
            {
                [spendRecord.Id] = timing
            });

        var handler = new GetMyAiUsageHistoryQueryHandler(repository.Object, resolver.Object);

        var result = await handler.Handle(
            new GetMyAiUsageHistoryQuery(spendRecord.UserId, new AiUsageHistoryFilter(null, null, null, null, null, null, null, null, null, null, null, null)),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle();
        var item = result.Value.Items[0];
        item.StartedAtUtc.Should().Be(timing.StartedAtUtc);
        item.CompletedAtUtc.Should().Be(timing.CompletedAtUtc);
        item.ProcessingDurationSeconds.Should().Be(90);
        result.Value.NextCursorCreatedAt.Should().Be(page.NextCursorCreatedAt);
        result.Value.NextCursorId.Should().Be(page.NextCursorId);

        repository.VerifyAll();
        resolver.VerifyAll();
    }
}
