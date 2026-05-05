namespace Application.Abstractions.Search;

/// <summary>
/// Image-search abstraction. Used to fetch a fresh real-world reference image
/// for the topic being drafted (e.g. "DJI Osmo Mobile 7" → an actual product shot)
/// so the image-gen model has a concrete visual anchor in addition to the brand's
/// past-post images. Currently has a single Brave-backed implementation.
/// </summary>
public interface IImageSearchClient
{
    Task<IReadOnlyList<ImageSearchHit>> SearchImagesAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken);
}

/// <summary>
/// One image returned from an image-search provider. <see cref="ImageUrl"/> is the
/// direct CDN bytes (jpg/png/webp); <see cref="ThumbnailUrl"/> is a smaller variant
/// served by the search provider's own CDN; <see cref="SourcePageUrl"/> is the page
/// the image appears on (useful for attribution / dedup).
/// </summary>
public sealed record ImageSearchHit(
    string ImageUrl,
    string? ThumbnailUrl,
    string? SourcePageUrl,
    string? Title,
    int? Width,
    int? Height);
