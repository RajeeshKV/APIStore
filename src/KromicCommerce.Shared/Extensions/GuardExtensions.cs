using System.Runtime.CompilerServices;

namespace KromicCommerce.Shared.Extensions;

/// <summary>
/// Lightweight guard helpers to replace repetitive null/empty checks in domain constructors.
/// </summary>
public static class GuardExtensions
{
    public static string NotNullOrWhiteSpace(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must not be null or whitespace.", paramName);
        return value;
    }

    public static T NotNull<T>(
        [NotNull] T? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value;
    }

    public static decimal GreaterThanOrEqualToZero(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(paramName, "Value must be >= 0.");
        return value;
    }

    public static decimal GreaterThanZero(
        decimal value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(paramName, "Value must be > 0.");
        return value;
    }
}
