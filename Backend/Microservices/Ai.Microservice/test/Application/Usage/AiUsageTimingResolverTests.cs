using System.Text.Json;
using Application.Billing;
using Application.Usage;
using Application.Usage.Models;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using Moq;

namespace AiMicroservice.Tests.Application.Usage;

public sealed class AiUsageTimingResolverTests
{
    [Fact]
    public async Task ResolveAsync_ShouldReturnImageTiming_WhenImageTaskExists()
    {
        var chatId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var spendRecord = CreateSpendRecord(Guid.NewGuid(), CoinReferenceTypes.ChatImage, chatId);
        var chat = CreateChat(chatId, correlationId);
        var task = new ImageTask
        {
            CorrelationId = correlationId,
            CreatedAt = new DateTime(2026, 5, 3, 10, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 5, 3, 10, 1, 30, DateTimeKind.Utc)
        };

        var resolver = CreateResolver(
            chats: [chat],
            imageTasks: [task],
            videoTasks: []);

        var result = await resolver.ResolveAsync([spendRecord], CancellationToken.None);

        result[spendRecord.Id].StartedAtUtc.Should().Be(task.CreatedAt);
        result[spendRecord.Id].CompletedAtUtc.Should().Be(task.CompletedAt);
        result[spendRecord.Id].ProcessingDurationSeconds.Should().Be(90);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnVideoTiming_WhenVideoTaskExists()
    {
        var chatId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var spendRecord = CreateSpendRecord(Guid.NewGuid(), CoinReferenceTypes.ChatVideo, chatId);
        var chat = CreateChat(chatId, correlationId);
        var task = new VideoTask
        {
            CorrelationId = correlationId,
            CreatedAt = new DateTime(2026, 5, 3, 11, 0, 0, DateTimeKind.Utc),
            CompletedAt = new DateTime(2026, 5, 3, 11, 0, 44, DateTimeKind.Utc)
        };

        var resolver = CreateResolver(
            chats: [chat],
            imageTasks: [],
            videoTasks: [task]);

        var result = await resolver.ResolveAsync([spendRecord], CancellationToken.None);

        result[spendRecord.Id].StartedAtUtc.Should().Be(task.CreatedAt);
        result[spendRecord.Id].CompletedAtUtc.Should().Be(task.CompletedAt);
        result[spendRecord.Id].ProcessingDurationSeconds.Should().Be(44);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnStartedAtOnly_WhenTaskIsNotCompleted()
    {
        var chatId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var spendRecord = CreateSpendRecord(Guid.NewGuid(), CoinReferenceTypes.ChatImage, chatId);
        var chat = CreateChat(chatId, correlationId);
        var task = new ImageTask
        {
            CorrelationId = correlationId,
            CreatedAt = new DateTime(2026, 5, 3, 12, 0, 0, DateTimeKind.Utc),
            CompletedAt = null
        };

        var resolver = CreateResolver(
            chats: [chat],
            imageTasks: [task],
            videoTasks: []);

        var result = await resolver.ResolveAsync([spendRecord], CancellationToken.None);

        result[spendRecord.Id].StartedAtUtc.Should().Be(task.CreatedAt);
        result[spendRecord.Id].CompletedAtUtc.Should().BeNull();
        result[spendRecord.Id].ProcessingDurationSeconds.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnNullTiming_WhenChatConfigIsInvalid()
    {
        var chatId = Guid.NewGuid();
        var spendRecord = CreateSpendRecord(Guid.NewGuid(), CoinReferenceTypes.ChatImage, chatId);
        var chat = new Chat
        {
            Id = chatId,
            Config = "{invalid-json"
        };

        var resolver = CreateResolver(
            chats: [chat],
            imageTasks: [],
            videoTasks: []);

        var result = await resolver.ResolveAsync([spendRecord], CancellationToken.None);

        result[spendRecord.Id].StartedAtUtc.Should().BeNull();
        result[spendRecord.Id].CompletedAtUtc.Should().BeNull();
        result[spendRecord.Id].ProcessingDurationSeconds.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnNullTiming_ForCaptionGeneration()
    {
        var spendRecord = CreateSpendRecord(Guid.NewGuid(), CoinReferenceTypes.CaptionBatch, Guid.NewGuid());
        var resolver = CreateResolver(chats: [], imageTasks: [], videoTasks: []);

        var result = await resolver.ResolveAsync([spendRecord], CancellationToken.None);

        result[spendRecord.Id].StartedAtUtc.Should().BeNull();
        result[spendRecord.Id].CompletedAtUtc.Should().BeNull();
        result[spendRecord.Id].ProcessingDurationSeconds.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ShouldBatchLoadChatsAndTasks_ForMixedPage()
    {
        var imageRecords = Enumerable.Range(0, 10)
            .Select(index => CreateSpendRecord(Guid.NewGuid(), CoinReferenceTypes.ChatImage, Guid.NewGuid()))
            .ToList();
        var videoRecords = Enumerable.Range(0, 10)
            .Select(index => CreateSpendRecord(Guid.NewGuid(), CoinReferenceTypes.ChatVideo, Guid.NewGuid()))
            .ToList();
        var records = imageRecords.Concat(videoRecords).ToList();

        var chats = records
            .Select((record, index) => CreateChat(Guid.Parse(record.ReferenceId), Guid.NewGuid()))
            .ToList();
        var imageTasks = chats.Take(10)
            .Select(chat => new ImageTask
            {
                CorrelationId = ReadCorrelationId(chat.Config!),
                CreatedAt = new DateTime(2026, 5, 3, 13, 0, 0, DateTimeKind.Utc),
                CompletedAt = new DateTime(2026, 5, 3, 13, 0, 5, DateTimeKind.Utc)
            })
            .ToList();
        var videoTasks = chats.Skip(10)
            .Select(chat => new VideoTask
            {
                CorrelationId = ReadCorrelationId(chat.Config!),
                CreatedAt = new DateTime(2026, 5, 3, 13, 5, 0, DateTimeKind.Utc),
                CompletedAt = new DateTime(2026, 5, 3, 13, 5, 9, DateTimeKind.Utc)
            })
            .ToList();

        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        chatRepository
            .Setup(repository => repository.GetByIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 20),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chats);

        var imageTaskRepository = new Mock<IImageTaskRepository>(MockBehavior.Strict);
        imageTaskRepository
            .Setup(repository => repository.GetByCorrelationIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(imageTasks);

        var videoTaskRepository = new Mock<IVideoTaskRepository>(MockBehavior.Strict);
        videoTaskRepository
            .Setup(repository => repository.GetByCorrelationIdsAsync(
                It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(videoTasks);

        var resolver = new AiUsageTimingResolver(
            chatRepository.Object,
            imageTaskRepository.Object,
            videoTaskRepository.Object);

        await resolver.ResolveAsync(records, CancellationToken.None);

        chatRepository.Verify(repository => repository.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        imageTaskRepository.Verify(repository => repository.GetByCorrelationIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        videoTaskRepository.Verify(repository => repository.GetByCorrelationIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AiUsageTimingResolver CreateResolver(
        IReadOnlyList<Chat> chats,
        IReadOnlyList<ImageTask> imageTasks,
        IReadOnlyList<VideoTask> videoTasks)
    {
        var chatRepository = new Mock<IChatRepository>(MockBehavior.Strict);
        chatRepository
            .Setup(repository => repository.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(chats);

        var imageTaskRepository = new Mock<IImageTaskRepository>(MockBehavior.Strict);
        imageTaskRepository
            .Setup(repository => repository.GetByCorrelationIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(imageTasks);

        var videoTaskRepository = new Mock<IVideoTaskRepository>(MockBehavior.Strict);
        videoTaskRepository
            .Setup(repository => repository.GetByCorrelationIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(videoTasks);

        return new AiUsageTimingResolver(
            chatRepository.Object,
            imageTaskRepository.Object,
            videoTaskRepository.Object);
    }

    private static AiSpendRecord CreateSpendRecord(Guid id, string referenceType, Guid chatId)
    {
        return new AiSpendRecord
        {
            Id = id,
            ReferenceType = referenceType,
            ReferenceId = chatId.ToString()
        };
    }

    private static Chat CreateChat(Guid chatId, Guid correlationId)
    {
        return new Chat
        {
            Id = chatId,
            Config = JsonSerializer.Serialize(new { CorrelationId = correlationId })
        };
    }

    private static Guid ReadCorrelationId(string config)
    {
        using var document = JsonDocument.Parse(config);
        return document.RootElement.GetProperty("CorrelationId").GetGuid();
    }
}
