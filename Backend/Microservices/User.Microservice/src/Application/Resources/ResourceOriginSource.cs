namespace Application.Resources;

public static class ResourceOriginSource
{
    private const int MaxOriginSourceUrlResponseLength = 8192;

    public static string? NormalizeForStorage(string? value)
    {
        var normalized = Normalize(value);
        return IsDataUrl(normalized) ? null : normalized;
    }

    public static string? NormalizeForResponse(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        if (IsDataUrl(normalized) || normalized.Length > MaxOriginSourceUrlResponseLength)
        {
            return null;
        }

        return normalized;
    }

    public static bool IsDataUrl(string? value) =>
        value?.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase) == true;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
