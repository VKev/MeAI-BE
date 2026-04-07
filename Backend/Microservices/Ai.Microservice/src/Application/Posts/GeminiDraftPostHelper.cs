using System.Text.RegularExpressions;

namespace Application.Posts;

internal static partial class GeminiDraftPostHelper
{
    private const string DefaultPostType = "posts";
    private static readonly char[] TitleTrimCharacters = [' ', '.', ',', ';', ':', '!', '?', '-', '"', '\''];
    private static readonly Regex HashtagRegex = HashtagPattern();
    private static readonly Regex CollapseWhitespaceRegex = CollapseWhitespacePattern();

    public static string NormalizePostType(string? postType)
    {
        if (string.IsNullOrWhiteSpace(postType))
        {
            return DefaultPostType;
        }

        var normalized = postType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "post" => "posts",
            "posts" => "posts",
            "reel" => "reels",
            "reels" => "reels",
            _ => postType.Trim()
        };
    }

    public static bool IsSupportedPostType(string? postType) =>
        string.Equals(postType, "posts", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(postType, "reels", StringComparison.OrdinalIgnoreCase);

    public static string? ResolveLanguageHint(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var normalized = language.Trim().ToLowerInvariant();
        return normalized switch
        {
            "vi" or "vn" or "vietnamese" => "Vietnamese",
            "en" or "english" => "English",
            _ => null
        };
    }

    public static IReadOnlyList<string> ExtractHashtags(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return Array.Empty<string>();
        }

        var matches = HashtagRegex.Matches(caption);
        if (matches.Count == 0)
        {
            return Array.Empty<string>();
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashtags = new List<string>(matches.Count);

        foreach (Match match in matches)
        {
            if (!match.Success || string.IsNullOrWhiteSpace(match.Value))
            {
                continue;
            }

            if (unique.Add(match.Value))
            {
                hashtags.Add(match.Value);
            }
        }

        return hashtags;
    }

    public static string NormalizeTitleContent(string caption)
    {
        var withoutHashtags = HashtagRegex.Replace(caption, string.Empty);
        var collapsed = CollapseWhitespaceRegex.Replace(withoutHashtags, " ");
        return string.IsNullOrWhiteSpace(collapsed) ? caption : collapsed.Trim();
    }

    public static string BuildDraftTitle(string caption)
    {
        var normalized = NormalizeTitleContent(caption)
            .ReplaceLineEndings(" ")
            .Trim();

        var words = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(8)
            .ToArray();

        if (words.Length == 0)
        {
            return "Draft Post";
        }

        return string.Join(' ', words).Trim(TitleTrimCharacters);
    }

    [GeneratedRegex("#([\\p{L}\\p{Mn}\\p{Nd}_]+)")]
    private static partial Regex HashtagPattern();

    [GeneratedRegex("\\s{2,}")]
    private static partial Regex CollapseWhitespacePattern();
}
