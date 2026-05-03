using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Infrastructure.Context;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace test;

public sealed class AiSpendRecordRepositoryTests
{
    [Fact]
    public async Task GetHistoryAsync_ShouldFilterByUserActionStatusWorkspaceAndDateRange()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var fromUtc = new DateTime(2026, 05, 03, 9, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 05, 03, 11, 0, 0, DateTimeKind.Utc);

        dbContext.AiSpendRecords.AddRange(
            CreateRecord(userId, workspaceId, new DateTime(2026, 05, 03, 10, 0, 0, DateTimeKind.Utc), actionType: "image_generation", status: "Debited"),
            CreateRecord(userId, workspaceId, new DateTime(2026, 05, 03, 10, 30, 0, DateTimeKind.Utc), actionType: "video_generation", status: "Refunded"),
            CreateRecord(otherUserId, workspaceId, new DateTime(2026, 05, 03, 10, 15, 0, DateTimeKind.Utc), actionType: "image_generation", status: "Debited"),
            CreateRecord(userId, Guid.NewGuid(), new DateTime(2026, 05, 03, 10, 20, 0, DateTimeKind.Utc), actionType: "image_generation", status: "Debited"),
            CreateRecord(userId, workspaceId, new DateTime(2026, 05, 03, 11, 0, 0, DateTimeKind.Utc), actionType: "image_generation", status: "Debited"));
        await dbContext.SaveChangesAsync();

        var repository = new AiSpendRecordRepository(dbContext);

        var page = await repository.GetHistoryAsync(
            new AiSpendRecordHistoryQuery(
                UserId: userId,
                FromUtc: fromUtc,
                ToUtc: toUtc,
                ActionType: "image_generation",
                Status: "debited",
                WorkspaceId: workspaceId,
                Provider: "kie",
                Model: "nano-banana-pro",
                ReferenceType: "chat_image",
                CursorCreatedAt: null,
                CursorId: null,
                Limit: 20),
            CancellationToken.None);

        page.Items.Should().ContainSingle();
        page.Items[0].UserId.Should().Be(userId);
        page.Items[0].CreatedAt.Should().Be(new DateTime(2026, 05, 03, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetHistoryAsync_ShouldUseStableCursorPagination_WhenMultipleRowsShareTimestamp()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var timestamp = new DateTime(2026, 05, 03, 10, 0, 0, DateTimeKind.Utc);
        var id1 = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
        var id2 = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
        var id3 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

        dbContext.AiSpendRecords.AddRange(
            CreateRecord(userId, null, timestamp, id1),
            CreateRecord(userId, null, timestamp, id2),
            CreateRecord(userId, null, timestamp, id3));
        await dbContext.SaveChangesAsync();

        var repository = new AiSpendRecordRepository(dbContext);

        var firstPage = await repository.GetHistoryAsync(
            new AiSpendRecordHistoryQuery(userId, null, null, null, null, null, null, null, null, null, null, 2),
            CancellationToken.None);

        var secondPage = await repository.GetHistoryAsync(
            new AiSpendRecordHistoryQuery(
                userId,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                firstPage.NextCursorCreatedAt,
                firstPage.NextCursorId,
                2),
            CancellationToken.None);

        firstPage.Items.Select(x => x.Id).Should().ContainInOrder(id1, id2);
        secondPage.Items.Select(x => x.Id).Should().ContainSingle().Which.Should().Be(id3);
        firstPage.Items.Select(x => x.Id).Should().NotIntersectWith(secondPage.Items.Select(x => x.Id));
    }

    private static MyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MyDbContext(options);
    }

    private static AiSpendRecord CreateRecord(
        Guid userId,
        Guid? workspaceId,
        DateTime createdAt,
        Guid? id = null,
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
            CreatedAt = createdAt,
            UpdatedAt = createdAt.AddMinutes(1)
        };
}
