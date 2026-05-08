namespace Application.Billing;

// Canonical strings used by the pricing catalog + debit/refund reasons. Kept here (not in
// Domain) so all BE callers agree on casing.
public static class CoinActionTypes
{
    public const string ImageGeneration = "image_generation";
    public const string ImageReframeVariant = "image_reframe_variant";
    public const string VideoGeneration = "video_generation";
    public const string CaptionGeneration = "caption_generation";
    public const string PostEnhancement = "post_enhancement";
    public const string FormulaGeneration = "formula_generation";
}

public static class CoinDebitReasons
{
    public const string ImageGenerationDebit = "ai.image_generation.debit";
    public const string ImageGenerationRefund = "ai.image_generation.refund";
    public const string VideoGenerationDebit = "ai.video_generation.debit";
    public const string VideoGenerationRefund = "ai.video_generation.refund";
    public const string CaptionGenerationDebit = "ai.caption_generation.debit";
    public const string CaptionGenerationRefund = "ai.caption_generation.refund";
    public const string PostEnhancementDebit = "ai.post_enhancement.debit";
    public const string PostEnhancementRefund = "ai.post_enhancement.refund";
    public const string FormulaGenerationDebit = "ai.formula_generation.debit";
    public const string FormulaGenerationRefund = "ai.formula_generation.refund";
}

public static class CoinReferenceTypes
{
    public const string ChatImage = "chat_image";
    public const string ChatVideo = "chat_video";
    public const string CaptionBatch = "caption_batch";
    public const string PostEnhancement = "post_enhancement";
    public const string FormulaGeneration = "formula_generation";
}

public static class AiSpendProviders
{
    public const string Kie = "kie";
    public const string OpenRouter = "openrouter";
}

public static class AiSpendStatuses
{
    public const string Debited = "debited";
    public const string Refunded = "refunded";
}
