namespace Application.Resources;

internal static class ResourceStorageKey
{
    internal static string Build(Guid userId, Guid resourceId) =>
        $"resources/{userId}/{resourceId}";
}
