using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Feed;

public interface IFeedResourceUsageService
{
    Task<Result<IReadOnlySet<Guid>>> GetActiveResourceIdsAsync(CancellationToken cancellationToken);
}
