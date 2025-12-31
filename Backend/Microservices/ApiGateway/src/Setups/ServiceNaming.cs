namespace src.Setups;

internal static class ServiceNaming
{
    internal static string ToServiceSegment(string prefix)
    {
        var lower = prefix.ToLowerInvariant();
        var parts = lower.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }
}
