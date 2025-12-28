using System.Collections.Generic;

namespace WebApi.Contracts;

public sealed record MessageResponse(string Message);

public sealed record HealthResponse(string Status);

public sealed record DebugHeadersResponse(
    IReadOnlyDictionary<string, string> Headers,
    string Scheme,
    string Host,
    string Path);
