namespace SharedLibrary.Common.Resources;

public sealed record ResourceProvenanceMetadata(
    string? OriginKind,
    Guid? OriginChatSessionId = null,
    Guid? OriginChatId = null,
    string? OriginSourceUrl = null)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(OriginKind) &&
        !OriginChatSessionId.HasValue &&
        !OriginChatId.HasValue &&
        string.IsNullOrWhiteSpace(OriginSourceUrl);
}
