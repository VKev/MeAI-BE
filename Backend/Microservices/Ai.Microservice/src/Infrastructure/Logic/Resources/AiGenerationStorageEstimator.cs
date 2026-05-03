using Application.Abstractions.Resources;
using Infrastructure.Configs;
using Microsoft.Extensions.Options;

namespace Infrastructure.Logic.Resources;

public sealed class AiGenerationStorageEstimator : IAiGenerationStorageEstimator
{
    private const long BytesPerMb = 1024L * 1024L;
    private readonly GenerationStorageEstimates _options;

    public AiGenerationStorageEstimator(IOptions<GenerationStorageEstimates> options)
    {
        _options = options.Value;
    }

    public long EstimateImageGenerationBytes(string model, string resolution, int expectedResultCount)
    {
        var normalizedResolution = string.IsNullOrWhiteSpace(resolution) ? "1K" : resolution.Trim();
        var mb = _options.ImagesByResolutionMb.TryGetValue(normalizedResolution, out var configuredMb)
            ? configuredMb
            : _options.ImagesByResolutionMb["1K"];

        return Math.Max(1, expectedResultCount) * mb * BytesPerMb;
    }

    public long EstimateVideoGenerationBytes(string model)
    {
        var normalizedModel = string.IsNullOrWhiteSpace(model) ? "veo3_fast" : model.Trim();
        var mb = _options.VideosByModelMb.TryGetValue(normalizedModel, out var configuredMb)
            ? configuredMb
            : _options.VideosByModelMb["veo3_fast"];

        return mb * BytesPerMb;
    }
}
