namespace Application.Billing;

public static class VideoGenerationSettings
{
    public static int? NormalizeDuration(string model, int? duration)
    {
        if (string.Equals(model, "gemini-omni-video", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDurationOption(duration, 4, 4, 6, 8, 10);
        }

        if (string.Equals(model, "grok-imagine-video-1-5-preview", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDurationRange(duration, 8, 1, 15);
        }

        if (string.Equals(model, "bytedance/seedance-2", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDurationRange(duration, 5, 4, 15);
        }

        if (string.Equals(model, "veo-3-1", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return duration;
    }

    private static int NormalizeDurationRange(int? duration, int fallback, int minimum, int maximum)
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
