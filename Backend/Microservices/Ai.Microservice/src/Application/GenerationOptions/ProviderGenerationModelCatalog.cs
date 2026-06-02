namespace Application.GenerationOptions;

public static class ProviderGenerationModelCatalog
{
    private static readonly string[] FullImageRatios =
        ["1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9"];

    private static readonly string[] UltraWideImageRatios =
        ["1:1", "2:3", "3:2", "3:4", "4:3", "4:5", "5:4", "9:16", "16:9", "21:9", "1:4", "4:1", "1:8", "8:1"];

    private static readonly string[] FluxRatios =
        ["1:1", "4:3", "3:4", "16:9", "9:16", "3:2", "2:3"];

    private static readonly string[] GrokRatios =
        ["2:3", "3:2", "1:1", "16:9", "9:16"];

    private static readonly string[] IdeogramRatios =
        ["1:1", "4:3", "3:4", "16:9", "9:16"];

    private static readonly string[] IdeogramReframeRatios =
        ["1:1", "4:3", "3:4", "16:9", "9:16"];

    private static readonly string[] StandardImageRatios =
        ["1:1", "4:3", "3:4", "16:9", "9:16"];

    private static readonly string[] GptImage15Ratios =
        ["1:1", "2:3", "3:2"];

    private static readonly string[] GptImage2Ratios =
        ["1:1", "3:4", "4:3", "9:16", "16:9", "auto"];

    private static readonly string[] VeoDimensions = ["16:9", "9:16", "auto"];

    private static readonly string[] VideoRatios = ["16:9", "9:16", "1:1"];

    private static readonly string[] ExtendedVideoRatios =
        ["16:9", "9:16", "1:1", "4:3", "3:4", "21:9"];

    private static readonly string[] GrokVideoRatios =
        ["2:3", "3:2", "1:1", "16:9", "9:16"];

    private static readonly string[] Grok15VideoRatios =
        ["auto", "1:1", "16:9", "9:16", "4:3", "3:4", "3:2", "2:3"];

    private static readonly string[] ImageQualities = ["1K", "2K", "4K"];

    private static readonly string[] ImageQualities2K = ["1K", "2K"];

    private static readonly string[] SeedreamQualities = ["basic", "high"];

    private static readonly string[] GptImage15Qualities = ["medium", "high"];

    private static readonly string[] VideoQualities = ["720p", "1080p"];

    public static readonly IReadOnlyList<ProviderGenerationModelOption> Models =
    [
        new("kie", "image", "nano-banana-pro", "Nano Banana Pro", "Google Gemini 3 Pro image generation and editing", FullImageRatios, ImageQualities, true, 10),
        new("kie", "image", "nano-banana-2", "Nano Banana 2", "Google Nano Banana 2 with broad aspect support", UltraWideImageRatios, ImageQualities, true, 20),
        new("kie", "image", "google/nano-banana", "Nano Banana", "Google fast text-to-image generation", FullImageRatios, [], false, 30),
        new("kie", "image", "google/nano-banana-edit", "Nano Banana Edit", "Google prompt-based image editing", FullImageRatios, [], false, 40),
        new("kie", "image", "google/imagen4-fast", "Imagen 4 Fast", "Google fast Imagen 4 generation", StandardImageRatios, [], false, 50),
        new("kie", "image", "google/imagen4", "Imagen 4", "Google balanced Imagen 4 generation", StandardImageRatios, [], false, 60),
        new("kie", "image", "google/imagen4-ultra", "Imagen 4 Ultra", "Google highest quality Imagen 4 generation", StandardImageRatios, [], false, 70),
        new("kie", "image", "bytedance/seedream", "Seedream 3.0", "ByteDance text-to-image generation", IdeogramRatios, [], false, 80),
        new("kie", "image", "bytedance/seedream-v4-text-to-image", "Seedream 4.0", "ByteDance 1K-4K text-to-image generation", FullImageRatios, ImageQualities, true, 90),
        new("kie", "image", "bytedance/seedream-v4-edit", "Seedream 4.0 Edit", "ByteDance image editing with references", FullImageRatios, ImageQualities, true, 100),
        new("kie", "image", "seedream/4.5-text-to-image", "Seedream 4.5", "ByteDance high-quality text-to-image", FullImageRatios, SeedreamQualities, true, 110),
        new("kie", "image", "seedream/4.5-edit", "Seedream 4.5 Edit", "ByteDance reference-guided image editing", FullImageRatios, SeedreamQualities, true, 120),
        new("kie", "image", "seedream/5-lite-text-to-image", "Seedream 5 Lite", "ByteDance efficient multimodal image generation", FullImageRatios, SeedreamQualities, true, 130),
        new("kie", "image", "seedream/5-lite-image-to-image", "Seedream 5 Lite Edit", "ByteDance image-to-image generation", FullImageRatios, SeedreamQualities, true, 140),
        new("kie", "image", "z-image", "Z-Image", "Efficient text-to-image generation", StandardImageRatios, [], false, 150),
        new("kie", "image", "grok-imagine/text-to-image", "Grok Imagine", "xAI photorealistic text-to-image", GrokRatios, [], false, 160),
        new("kie", "image", "grok-imagine/image-to-image", "Grok Imagine Edit", "xAI image-to-image generation", GrokRatios, [], false, 170),
        new("kie", "image", "gpt-image/1.5-text-to-image", "GPT Image 1.5", "OpenAI text-to-image with strong instruction following", GptImage15Ratios, GptImage15Qualities, true, 180),
        new("kie", "image", "gpt-image/1.5-image-to-image", "GPT Image 1.5 Edit", "OpenAI image editing with references", GptImage15Ratios, GptImage15Qualities, true, 190),
        new("kie", "image", "gpt-image-2-text-to-image", "GPT Image 2", "OpenAI GPT Image 2 text-to-image", GptImage2Ratios, ImageQualities, true, 200),
        new("kie", "image", "gpt-image-2-image-to-image", "GPT Image 2 Edit", "OpenAI GPT Image 2 image-to-image", GptImage2Ratios, ImageQualities, true, 210),
        new("kie", "image", "ideogram/v3-text-to-image", "Ideogram V3", "Creative generation with strong text rendering", IdeogramRatios, [], false, 220),
        new("kie", "image", "ideogram/v3-reframe", "Ideogram V3 Reframe", "Ideogram intelligent aspect-ratio reframing", IdeogramReframeRatios, [], false, 230),
        new("kie", "image", "ideogram/v3-remix", "Ideogram V3 Remix", "Ideogram reference-guided remixing", IdeogramRatios, [], false, 240),
        new("kie", "image", "flux-2/pro-text-to-image", "Flux 2 Pro", "Flux advanced text-to-image generation", FluxRatios, ImageQualities2K, true, 250),
        new("kie", "image", "flux-2/pro-image-to-image", "Flux 2 Pro Edit", "Flux Pro image-to-image generation", FluxRatios, ImageQualities2K, true, 260),
        new("kie", "image", "flux-2/flex-text-to-image", "Flux 2 Flex", "Flux flexible text-to-image generation", FluxRatios, ImageQualities2K, true, 270),
        new("kie", "image", "flux-2/flex-image-to-image", "Flux 2 Flex Edit", "Flux flexible image-to-image generation", FluxRatios, ImageQualities2K, true, 280),
        new("kie", "image", "qwen/text-to-image", "Qwen Image", "Qwen text-to-image generation", IdeogramRatios, [], false, 290),
        new("kie", "image", "qwen/image-to-image", "Qwen Image to Image", "Qwen image-to-image generation", IdeogramRatios, [], false, 300),
        new("kie", "image", "qwen/image-edit", "Qwen Image Edit", "Qwen prompt-based image editing", IdeogramRatios, [], false, 310),
        new("kie", "image", "qwen2/image-edit", "Qwen2 Image Edit", "Qwen2 prompt-based image editing", FullImageRatios, [], false, 320),
        new("kie", "image", "wan/2-7-image", "Wan 2.7 Image", "Wan 2.7 image generation", UltraWideImageRatios, ImageQualities, true, 330),
        new("kie", "image", "wan/2-7-image-pro", "Wan 2.7 Image Pro", "Wan 2.7 high-quality image generation", UltraWideImageRatios, ImageQualities, true, 340),
        new("kie", "video", "gemini-omni-video", "Gemini Omni Video", "Google multimodal video generation", ["16:9", "9:16"], ["720p", "1080p", "4k"], true, 10),
        new("kie", "video", "grok-imagine-video-1-5-preview", "Grok Imagine Video 1.5 Preview", "xAI video generation with optional image guidance", Grok15VideoRatios, ["480p", "720p"], true, 20),
        new("kie", "video", "veo-3-1", "Veo 3.1", "Google Veo 3.1 video generation with Lite, Fast, and Quality tiers", VeoDimensions, ["lite", "fast", "quality"], true, 30),
        new("kie", "video", "bytedance/seedance-2", "Seedance 2.0", "ByteDance Seedance 2 multimodal video generation", ExtendedVideoRatios, ["480p", "720p", "1080p"], true, 40),
        new("kie", "video", "veo3_fast", "Veo 3.1 Fast", "Google - fast video generation", VeoDimensions, [], false, 10),
        new("kie", "video", "veo3", "Veo 3.1 Quality", "Google - highest fidelity video", VeoDimensions, [], false, 20),
        new("kie", "video", "veo3_lite", "Veo 3.1 Lite", "Google - cost-effective for high volume", VeoDimensions, [], false, 30),
        new("kie", "video", "grok-imagine/text-to-video", "Grok Imagine Video", "xAI text-to-video generation", GrokVideoRatios, ["480p", "720p"], true, 40),
        new("kie", "video", "grok-imagine/image-to-video", "Grok Imagine Image Video", "xAI image-to-video generation", GrokVideoRatios, ["480p", "720p"], true, 50),
        new("kie", "video", "kling-3.0/video", "Kling 3.0", "Kling 3.0 video generation", VideoRatios, ["std", "pro"], true, 60),
        new("kie", "video", "kling-2.6/text-to-video", "Kling 2.6", "Kling text-to-video generation", VideoRatios, [], false, 70),
        new("kie", "video", "kling-2.6/image-to-video", "Kling 2.6 Image Video", "Kling image-to-video generation", VideoRatios, [], false, 80),
        new("kie", "video", "kling/v2-5-turbo-text-to-video-pro", "Kling 2.5 Turbo", "Kling turbo text-to-video generation", VideoRatios, [], false, 90),
        new("kie", "video", "kling/v2-1-master-text-to-video", "Kling 2.1 Master", "Kling master text-to-video generation", VideoRatios, [], false, 100),
        new("kie", "video", "kling/v2-1-master-image-to-video", "Kling 2.1 Master Image", "Kling master image-to-video generation", VideoRatios, [], false, 110),
        new("kie", "video", "kling/v2-1-pro", "Kling 2.1 Pro", "Kling pro image-to-video generation", VideoRatios, [], false, 120),
        new("kie", "video", "kling/v2-1-standard", "Kling 2.1 Standard", "Kling standard image-to-video generation", VideoRatios, [], false, 130),
        new("kie", "video", "bytedance/seedance-2-fast", "Seedance 2.0 Fast", "ByteDance fast Seedance 2 generation", ExtendedVideoRatios, ["480p", "720p"], true, 150),
        new("kie", "video", "bytedance/seedance-1.5-pro", "Seedance 1.5 Pro", "ByteDance text/image-to-video generation", ExtendedVideoRatios, ["480p", "720p", "1080p"], true, 160),
        new("kie", "video", "bytedance/v1-pro-text-to-video", "ByteDance V1 Pro", "ByteDance V1 Pro text-to-video", ExtendedVideoRatios, ["480p", "720p", "1080p"], true, 170),
        new("kie", "video", "bytedance/v1-pro-image-to-video", "ByteDance V1 Pro Image", "ByteDance V1 Pro image-to-video", VideoRatios, ["480p", "720p", "1080p"], true, 180),
        new("kie", "video", "bytedance/v1-pro-fast-image-to-video", "ByteDance V1 Pro Fast Image", "ByteDance fast image-to-video", VideoRatios, VideoQualities, true, 190),
        new("kie", "video", "bytedance/v1-lite-text-to-video", "ByteDance V1 Lite", "ByteDance V1 Lite text-to-video", ["16:9", "9:16", "1:1", "4:3", "3:4"], ["480p", "720p", "1080p"], true, 200),
        new("kie", "video", "bytedance/v1-lite-image-to-video", "ByteDance V1 Lite Image", "ByteDance V1 Lite image-to-video", VideoRatios, ["480p", "720p", "1080p"], true, 210),
        new("kie", "video", "hailuo/02-text-to-video-pro", "Hailuo Pro", "Hailuo Pro text-to-video generation", VideoRatios, [], false, 220),
        new("kie", "video", "hailuo/02-image-to-video-pro", "Hailuo Pro Image", "Hailuo Pro image-to-video generation", VideoRatios, [], false, 230),
        new("kie", "video", "hailuo/02-text-to-video-standard", "Hailuo Standard", "Hailuo Standard text-to-video generation", VideoRatios, [], false, 240),
        new("kie", "video", "hailuo/02-image-to-video-standard", "Hailuo Standard Image", "Hailuo Standard image-to-video generation", VideoRatios, ["512P", "768P"], true, 250),
        new("kie", "video", "hailuo/2-3-image-to-video-pro", "Hailuo 2.3 Pro Image", "Hailuo 2.3 Pro image-to-video", VideoRatios, ["768P", "1080P"], true, 260),
        new("kie", "video", "hailuo/2-3-image-to-video-standard", "Hailuo 2.3 Standard Image", "Hailuo 2.3 Standard image-to-video", VideoRatios, ["768P", "1080P"], true, 270),
        new("kie", "video", "wan/2-2-a14b-text-to-video-turbo", "Wan 2.2 Turbo", "Wan 2.2 text-to-video generation", ["16:9", "9:16"], ["480p", "720p"], true, 280),
        new("kie", "video", "wan/2-2-a14b-image-to-video-turbo", "Wan 2.2 Turbo Image", "Wan 2.2 image-to-video generation", ["16:9", "9:16"], ["480p", "720p"], true, 290),
        new("kie", "video", "wan/2-5-text-to-video", "Wan 2.5", "Wan 2.5 text-to-video generation", VideoRatios, VideoQualities, true, 300),
        new("kie", "video", "wan/2-5-image-to-video", "Wan 2.5 Image", "Wan 2.5 image-to-video generation", VideoRatios, VideoQualities, true, 310),
        new("kie", "video", "wan/2-6-text-to-video", "Wan 2.6", "Wan 2.6 text-to-video generation", ["16:9", "9:16"], VideoQualities, true, 320),
        new("kie", "video", "wan/2-6-image-to-video", "Wan 2.6 Image", "Wan 2.6 image-to-video generation", ["16:9", "9:16"], VideoQualities, true, 330),
        new("kie", "video", "wan/2-6-flash-image-to-video", "Wan 2.6 Flash Image", "Wan 2.6 flash image-to-video generation", ["16:9", "9:16"], VideoQualities, true, 340),
        new("kie", "video", "wan/2-7-text-to-video", "Wan 2.7", "Wan 2.7 text-to-video generation", ["16:9", "9:16", "1:1", "4:3", "3:4"], VideoQualities, true, 350),
        new("kie", "video", "wan/2-7-image-to-video", "Wan 2.7 Image", "Wan 2.7 image-to-video generation", ["16:9", "9:16", "1:1", "4:3", "3:4"], VideoQualities, true, 360),
        new("kie", "video", "wan/2-7-r2v", "Wan 2.7 Reference", "Wan 2.7 reference-to-video generation", ["16:9", "9:16", "1:1", "4:3", "3:4"], VideoQualities, true, 370),
        new("kie", "video", "happyhorse/text-to-video", "HappyHorse", "HappyHorse text-to-video generation", ["16:9", "9:16", "1:1", "4:3", "3:4"], VideoQualities, true, 380),
        new("kie", "video", "happyhorse/image-to-video", "HappyHorse Image", "HappyHorse image-to-video generation", ["16:9", "9:16", "1:1", "4:3", "3:4"], VideoQualities, true, 390),
        new("kie", "video", "happyhorse/reference-to-video", "HappyHorse Reference", "HappyHorse reference-to-video generation", ["16:9", "9:16", "1:1", "4:3", "3:4"], VideoQualities, true, 400),
        new("kie", "video", "sora-2-text-to-video", "Sora 2", "OpenAI text-to-video through Kie market", ["16:9", "9:16"], [], false, 420)
    ];

    private static readonly HashSet<string> DefaultSeedModelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ModelKey("image", "nano-banana-2"),
        ModelKey("image", "ideogram/v3-text-to-image"),
        ModelKey("image", "gpt-image-2-text-to-image"),
        ModelKey("video", "gemini-omni-video"),
        ModelKey("video", "grok-imagine-video-1-5-preview"),
        ModelKey("video", "veo-3-1"),
        ModelKey("video", "bytedance/seedance-2")
    };

    private static readonly HashSet<string> RetiredDefaultSeedModelKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ModelKey("video", "veo3_fast"),
        ModelKey("video", "veo3"),
        ModelKey("video", "veo3_lite")
    };

    public static IReadOnlyList<ProviderGenerationModelOption> DefaultSeedModels =>
        Models
            .Where(model => DefaultSeedModelKeys.Contains(ModelKey(model.Mode, model.ModelId)))
            .OrderBy(model => model.Mode)
            .ThenBy(model => model.SortOrder)
            .ThenBy(model => model.Name)
            .ToList();

    public static IReadOnlyList<ProviderGenerationModelOption> GetModels(string? provider, string? mode)
    {
        var normalizedProvider = string.IsNullOrWhiteSpace(provider) ? "kie" : provider.Trim().ToLowerInvariant();
        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? null : mode.Trim().ToLowerInvariant();

        return Models
            .Where(model => string.Equals(model.Provider, normalizedProvider, StringComparison.OrdinalIgnoreCase))
            .Where(model => normalizedMode is null || string.Equals(model.Mode, normalizedMode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(model => model.Mode)
            .ThenBy(model => model.SortOrder)
            .ThenBy(model => model.Name)
            .ToList();
    }

    public static bool IsDefaultSeedModel(string mode, string modelId)
        => DefaultSeedModelKeys.Contains(ModelKey(mode, modelId));

    public static bool IsRetiredDefaultSeedModel(string mode, string modelId)
        => RetiredDefaultSeedModelKeys.Contains(ModelKey(mode, modelId));

    public static bool IsKnownModel(string mode, string modelId)
        => Models.Any(model =>
            string.Equals(model.Mode, mode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase));

    private static string ModelKey(string mode, string modelId) => $"{mode}:{modelId}";
}

public sealed record ProviderGenerationModelOption(
    string Provider,
    string Mode,
    string ModelId,
    string Name,
    string Description,
    IReadOnlyList<string> SupportedRatios,
    IReadOnlyList<string> SupportedQualities,
    bool SupportsResolution,
    int SortOrder);
