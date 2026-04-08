using System.Text.Json;
using System.Text.RegularExpressions;
using SharedLibrary.Common;
using SharedLibrary.Common.ResponseModel;

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

    public static Result<string> NormalizePlatformType(string? rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
        {
            return Result.Failure<string>(
                new Error("SocialMedia.InvalidType", "Each social media item must include a type or socialMediaId."));
        }

        var normalized = rawType.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized switch
        {
            "facebook" or "fb" => Result.Success("facebook"),
            "tiktok" => Result.Success("tiktok"),
            "instagram" or "ig" => Result.Success("ig"),
            "threads" => Result.Success("threads"),
            _ => Result.Failure<string>(
                new Error("SocialMedia.UnsupportedPlatform", "Only Facebook, Instagram, TikTok, and Threads are supported."))
        };
    }

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

    public static string? SerializeResourceIds(IReadOnlyList<Guid> resourceIds)
    {
        var normalized = resourceIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Select(id => id.ToString())
            .ToList();

        return normalized.Count == 0
            ? null
            : JsonSerializer.Serialize(normalized);
    }

    public static IReadOnlyList<Guid> ParseResourceIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<Guid>();
        }

        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(json);
            if (values is not null)
            {
                return values
                    .Select(ParseGuid)
                    .Where(id => id != Guid.Empty)
                    .Distinct()
                    .ToList();
            }
        }
        catch (JsonException)
        {
        }

        try
        {
            var values = JsonSerializer.Deserialize<List<Guid>>(json);
            if (values is null)
            {
                return Array.Empty<Guid>();
            }

            return values
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<Guid>();
        }
    }

    private static Guid ParseGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty;

    [GeneratedRegex("#([\\p{L}\\p{Mn}\\p{Nd}_]+)")]
    private static partial Regex HashtagPattern();

    [GeneratedRegex("\\s{2,}")]
    private static partial Regex CollapseWhitespacePattern();
}
