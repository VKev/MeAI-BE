using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Threads;

public interface IThreadsPublishService
{
    Task<Result<ThreadsPublishResult>> PublishAsync(
        ThreadsPublishRequest request,
        CancellationToken cancellationToken);
}

public sealed record ThreadsPublishRequest(
    string AccessToken,
    string ThreadsUserId,
    string Text,
    ThreadsPublishMedia? Media);

public sealed record ThreadsPublishMedia(
    string Url,
    string? ContentType);

public sealed record ThreadsPublishResult(
    string ThreadsUserId,
    string PostId);
