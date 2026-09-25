namespace KromicCommerce.Shared.Extensions;

public static class DateTimeExtensions
{
    /// <summary>
    /// Ensures a DateTime value is treated as UTC.
    /// Throws if the kind is Local — callers must be explicit about their time source.
    /// </summary>
    public static DateTime AsUtc(this DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => throw new InvalidOperationException(
                "Cannot convert a Local DateTime to UTC implicitly. Use DateTime.UtcNow or convert explicitly.")
        };
    }
}
