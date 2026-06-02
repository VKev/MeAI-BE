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
            var normalizedDuration = NormalizeDurationOption(duration, 4, 4, 6, 8, 10);
            return new VideoPricingSelection(
                $"{normalizedResolution}:{normalizedDuration}s",
                1);
        }

        if (string.Equals(model, "grok-imagine-video-1-5-preview", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedResolution = NormalizeResolution(resolution, "480p", "480p", "720p");
            var normalizedDuration = NormalizeDuration(duration, 8, 1, 15);
            return new VideoPricingSelection(
                $"{normalizedResolution}:{normalizedDuration}s",
                1);
        }

        if (string.Equals(model, "bytedance/seedance-2", StringComparison.OrdinalIgnoreCase))
        {
            return new VideoPricingSelection(
                NormalizeResolution(resolution, "720p", "480p", "720p", "1080p"),
                NormalizeDuration(duration, 5, 4, 15));
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

    private static int NormalizeDuration(int? duration, int fallback, int minimum, int maximum)
    {
        return duration.HasValue
            ? Math.Clamp(duration.Value, minimum, maximum)
            : fallback;
    }

    private static int NormalizeDurationOption(int? duration, int fallback, params int[] supported)
    {
        return duration.HasValue && supported.Contains(duration.Value)
            ? duration.Value
            : fallback;
    }
}

public sealed record VideoPricingSelection(
    string? CatalogVariant,
    int Quantity);
