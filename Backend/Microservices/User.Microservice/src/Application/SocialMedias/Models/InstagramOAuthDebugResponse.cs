using System.Collections.Generic;

namespace Application.SocialMedias.Models;

public sealed record InstagramOAuthDebugResponse(
    InstagramDebugTokenInfo? Token,
    IReadOnlyList<string> MissingPermissions,
    InstagramDebugPagesResponse? Pages,
    string? TokenError);

public sealed record InstagramDebugTokenInfo(
    bool IsValid,
    string? AppId,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<InstagramDebugGranularScope> GranularScopes);

public sealed record InstagramDebugGranularScope(
    string Scope,
    IReadOnlyList<string> TargetIds);

public sealed record InstagramDebugPagesResponse(
    int Count,
    IReadOnlyList<InstagramDebugPage> Pages,
    string? Error);

public sealed record InstagramDebugPage(
    string? Id,
    string? Name,
    IReadOnlyList<string> Tasks,
    bool HasAccessToken,
    string? InstagramBusinessAccountId,
    string? ConnectedInstagramAccountId);
