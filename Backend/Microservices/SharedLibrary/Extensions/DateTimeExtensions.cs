namespace SharedLibrary.Extensions;

public static class DateTimeExtensions
{
    /// <summary>
    /// Local time - dùng cho các cột 'timestamp without time zone'
    /// </summary>
    public static DateTime PostgreSqlNow => DateTime.Now;

    /// <summary>
    /// UTC time - dùng cho các cột 'timestamp with time zone'
    /// </summary>
    public static DateTime PostgreSqlUtcNow => DateTime.UtcNow;
}
