namespace Application.Billing;

public static class VideoPricingResolver
{
    public static VideoPricingSelection Resolve(
        string model,
        string? modelVariant,
        string? resolution,
        int? duration)
    {
        if (string.Equals(model, "gemini-omni-video", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedResolution = NormalizeResolution(resolution, "720p", "720p", "1080p", "4k");
            var normalizedDuration = VideoGenerationSettings.NormalizeDuration(model, duration) ?? 4;
            return new VideoPricingSelection(
                $"{normalizedResolution}:{normalizedDuration}s",
                1);
        }

        if (string.Equals(model, "grok-imagine-video-1-5-preview", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedResolution = NormalizeResolution(resolution, "480p", "480p", "720p");
            var normalizedDuration = VideoGenerationSettings.NormalizeDuration(model, duration) ?? 8;
            return new VideoPricingSelection(
                $"{normalizedResolution}:{normalizedDuration}s",
                1);
        }

        if (string.Equals(model, "bytedance/seedance-2", StringComparison.OrdinalIgnoreCase))
        {
            return new VideoPricingSelection(
                NormalizeResolution(resolution, "720p", "480p", "720p", "1080p"),
                VideoGenerationSettings.NormalizeDuration(model, duration) ?? 5);
        }

        return new VideoPricingSelection(
            string.IsNullOrWhiteSpace(modelVariant) ? null : modelVariant.Trim(),
            1);
    }

    private static string NormalizeResolution(string? resolution, string fallback, params string[] supported)
    {
        if (string.IsNullOrWhiteSpace(resolution))
        {
            return fallback;
        }

        return supported.FirstOrDefault(item =>
            item.Equals(resolution.Trim(), StringComparison.OrdinalIgnoreCase)) ?? fallback;
    }

}

public sealed record VideoPricingSelection(
    string? CatalogVariant,
    int Quantity);
