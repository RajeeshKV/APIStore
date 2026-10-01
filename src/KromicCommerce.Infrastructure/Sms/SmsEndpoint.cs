namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Builds absolute request URIs from a configurable base URL.
/// </summary>
/// <remarks>
/// The named <see cref="HttpClient"/> instances carry no <c>BaseAddress</c>, because the base
/// URL is per-provider configuration rather than a fixed constant. Combining the two here
/// keeps a trailing-slash difference in configuration from silently dropping the last path
/// segment.
/// </remarks>
internal static class SmsEndpoint
{
    public static Uri Build(string baseUrl, string relativePath)
    {
        var root = new Uri($"{baseUrl.TrimEnd('/')}/", UriKind.Absolute);
        return new Uri(root, relativePath.TrimStart('/'));
    }
}
