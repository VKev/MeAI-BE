namespace Application.Common;

internal readonly record struct FeedPaginationOptions(
    DateTime? CursorCreatedAt,
    Guid? CursorId,
    int Limit)
{
    public bool HasCursor => CursorCreatedAt.HasValue && CursorId.HasValue;
}

internal static class FeedPaginationSupport
{
    internal const int DefaultPageSize = 50;
    internal const int MaxPageSize = 100;

    public static FeedPaginationOptions Normalize(DateTime? cursorCreatedAt, Guid? cursorId, int? limit)
    {
        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        return new FeedPaginationOptions(cursorCreatedAt, cursorId, pageSize);
    }
}
