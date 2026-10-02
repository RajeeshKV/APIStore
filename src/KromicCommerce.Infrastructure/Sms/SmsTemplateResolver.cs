using System.Globalization;
using System.Text.RegularExpressions;
using KromicCommerce.Application.Abstractions.Sms;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Everything a gateway adapter needs about the template an administrator configured, resolved
/// fresh from the database on every send.
/// </summary>
/// <param name="Exists">False when the provider has no active template configured.</param>
/// <param name="AdminName">Admin-facing label, used for logging only.</param>
/// <param name="Reference">
/// The vendor-side registration identifier, which means a <b>different thing per provider</b>:
/// a 2Factor template <b>name</b>, a Twilio Verify <c>TemplateSid</c> (<c>HJ…</c>), a Free2SMS DLT
/// template id. This is the value an operator registers in the vendor portal, and it is never a
/// secret — it is safe to log so a rejected template can be diagnosed without a redeploy.
/// </param>
/// <param name="Body">Configured message body with placeholder tokens, or empty.</param>
/// <param name="Variables">
/// Placeholder tokens from <paramref name="Body"/> mapped to the values to substitute, in the
/// order the placeholders first appear. Gateways that address template variables positionally
/// (2Factor's <c>var1</c>, <c>var2</c>) are driven from this ordering; gateways that address them
/// by name (Brevo's <c>params</c>) are driven from the keys.
/// </param>
internal sealed record SmsTemplateResolution(
    bool Exists,
    string AdminName,
    string? Reference,
    string Body,
    IReadOnlyList<KeyValuePair<string, string>> Variables)
{
    internal static readonly SmsTemplateResolution None = new(false, string.Empty, null, string.Empty, []);

    /// <summary>True when the administrator registered a vendor template to send against.</summary>
    public bool HasReference => !string.IsNullOrWhiteSpace(Reference);

    /// <summary>True when this template supplies the message body itself.</summary>
    public bool HasBody => !string.IsNullOrWhiteSpace(Body);

    /// <summary>
    /// Renders the body with the OTP substituted. Used only by gateways that accept a rendered
    /// body; gateways driven by <see cref="Reference"/> ignore it.
    /// </summary>
    /// <remarks>
    /// Substitution is driven by <see cref="Variables"/> rather than by a fixed list of tokens, so
    /// every placeholder extracted from the body is also replaced. Enumerating them separately
    /// would let an unrecognised token reach the customer as a literal "{WHATEVER}".
    /// </remarks>
    public string Render()
    {
        if (!HasBody)
            return string.Empty;

        var rendered = Body;

        foreach (var pair in Variables)
            rendered = rendered.Replace(pair.Key, pair.Value, StringComparison.Ordinal);

        return rendered;
    }
}

/// <summary>The placeholder tokens an administrator writes into a template body.</summary>
internal static class SmsTemplateTokens
{
    public const string Otp = "{OTP}";
    public const string Expiry = "{EXPIRY_MINUTES}";
    public const string StoreName = "{STORE_NAME}";
}

/// <summary>
/// Resolves the active template for a provider into the shape that provider needs.
/// </summary>
/// <remarks>
/// <para>
/// Every adapter resolves the template <b>per request</b> rather than caching it at startup. That
/// is what lets an administrator register a template, or change which template is active, and see
/// it apply to the very next OTP with no restart and no cache invalidation. It also means the
/// template reference below is genuinely "read from the user's configuration during the request".
/// </para>
/// <para>
/// The placeholder tokens are extracted in first-appearance order so a gateway that numbers its
/// variables positionally gets a stable mapping from the body the administrator actually wrote,
/// instead of an adapter-specific vocabulary they would have to learn separately.
/// </para>
/// </remarks>
internal static class SmsTemplateResolver
{
    /// <summary>Placeholder tokens recognised in a template body, e.g. <c>{OTP}</c>.</summary>
    private static readonly Regex TokenPattern = new(@"\{([A-Z0-9_]+)\}", RegexOptions.Compiled);

    public static async Task<SmsTemplateResolution> ResolveAsync(
        ISmsTemplateStore store,
        SmsProviderKind provider,
        string otp,
        int expiryMinutes,
        CancellationToken cancellationToken)
    {
        var snapshot = await store.GetActiveAsync(provider, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return SmsTemplateResolution.None;

        return new SmsTemplateResolution(
            Exists: true,
            AdminName: snapshot.Name,
            // The vendor reference is the admin-entered identifier: for 2Factor this is the
            // template NAME that goes into the request, for Twilio the TemplateSid. Treating it
            // as opaque here is what keeps one template row usable by every provider.
            Reference: snapshot.HasExternalTemplate ? snapshot.ExternalTemplateId : null,
            Body: snapshot.Body,
            Variables: BuildVariables(snapshot.Body, otp, expiryMinutes));
    }

    /// <summary>
    /// Maps every recognised placeholder in <paramref name="body"/> to its delivery-time value,
    /// preserving the order the placeholders first appear so positional gateways stay stable.
    /// </summary>
    private static IReadOnlyList<KeyValuePair<string, string>> BuildVariables(
        string body, string otp, int expiryMinutes)
    {
        var expiry = expiryMinutes.ToString(CultureInfo.InvariantCulture);
        var ordered = new List<KeyValuePair<string, string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in TokenPattern.Matches(body ?? string.Empty))
        {
            var token = match.Value;
            if (!seen.Add(token))
                continue;

            var value = token switch
            {
                SmsTemplateTokens.Otp => otp,
                SmsTemplateTokens.Expiry => expiry,
                // Recognised, but the brand name is not available on the send path, so it renders
                // empty rather than leaving a literal "{STORE_NAME}" in the customer's message.
                SmsTemplateTokens.StoreName => string.Empty,
                // A token we do not recognise is still passed through, as an empty value, so a
                // template referring to something the gateway cannot fill fails visibly at the
                // gateway instead of silently rendering a literal "{WHATEVER}" to the customer.
                _ => string.Empty
            };

            ordered.Add(new KeyValuePair<string, string>(token, value));
        }

        return ordered;
    }
}
