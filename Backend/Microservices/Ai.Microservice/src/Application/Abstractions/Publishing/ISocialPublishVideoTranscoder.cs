using Application.Abstractions.Resources;
using SharedLibrary.Common.ResponseModel;

namespace Application.Abstractions.Publishing;

public interface ISocialPublishVideoTranscoder
{
    Task<Result<byte[]>> ConvertToMp4Async(
        UserResourcePresignResult resource,
        CancellationToken cancellationToken);
}
