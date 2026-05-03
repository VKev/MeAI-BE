namespace Application.Abstractions.Resources;

public interface IAiGenerationStorageEstimator
{
    long EstimateImageGenerationBytes(string model, string resolution, int expectedResultCount);

    long EstimateVideoGenerationBytes(string model);
}
