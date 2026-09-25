namespace KromicCommerce.Shared.Extensions;

public static class StringExtensions
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? ToUpperSnakeCase(this string? value)
    {
        if (value is null) return null;
        return string.Concat(value.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToUpperInvariant();
    }

    public static bool IsNullOrWhiteSpace([NotNullWhen(false)] this string? value) =>
        string.IsNullOrWhiteSpace(value);
}
