namespace KromicCommerce.Domain.Sms;

/// <summary>
/// The SMS gateway integrations this application supports.
/// Exactly one provider may be active at a time; see the application's
/// <c>ISmsProviderFactory</c> for how that rule is enforced at runtime.
/// </summary>
/// <remarks>
/// This set is closed and deliberate. Adding a value here is the only step needed to make a
/// new gateway selectable; every other place that must know the allowed providers
/// (configuration validation, the admin config endpoint, the settings projection) reads this
/// enum rather than repeating a list, so the integrations cannot drift apart.
/// </remarks>
public enum SmsProviderKind
{
    /// <summary>SMS is not configured. A no-op provider is used and OTP sending is refused.</summary>
    None = 0,

    /// <summary>2Factor — API-key authenticated SMS gateway.</summary>
    TwoFactor = 1,

    /// <summary>Free2SMS — bearer-token JSON REST API with DLT template matching.</summary>
    Free2Sms = 2,

    /// <summary>Twilio Verify — hosted OTP service over the Verify v2 REST API.</summary>
    Twilio = 3
}

/// <summary>Helpers over the closed <see cref="SmsProviderKind"/> set.</summary>
public static class SmsProviderKinds
{
    /// <summary>Every provider an administrator is allowed to select. Excludes <see cref="SmsProviderKind.None"/>.</summary>
    public static IReadOnlyList<SmsProviderKind> Selectable { get; } =
    [
        SmsProviderKind.TwoFactor,
        SmsProviderKind.Free2Sms,
        SmsProviderKind.Twilio
    ];

    /// <summary>
    /// Parses a configured/administrator-supplied name, accepting the spellings the vendors
    /// and this codebase use for the same gateway. <see cref="SmsProviderKind.None"/> is returned
    /// for an explicit "none" (SMS switched off); <c>null</c> is returned for anything else so
    /// callers can report a precise error rather than silently falling back to a provider.
    /// </summary>
    public static SmsProviderKind? Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var normalized = name.Trim().Replace(" ", string.Empty).Replace("_", string.Empty)
            .Replace("-", string.Empty);

        return normalized.ToLowerInvariant() switch
        {
            "none" or "disabled" => SmsProviderKind.None,
            "2factor" or "2factorsms" => SmsProviderKind.TwoFactor,
            "free2sms" or "free2smsms" => SmsProviderKind.Free2Sms,
            "twilio" or "twilioverify" => SmsProviderKind.Twilio,
            _ => null
        };
    }

    /// <summary>The canonical name written to configuration and shown on admin surfaces.</summary>
    public static string ToName(this SmsProviderKind kind) => kind switch
    {
        SmsProviderKind.TwoFactor => "2Factor",
        SmsProviderKind.Free2Sms => "Free2SMS",
        SmsProviderKind.Twilio => "Twilio",
        _ => "None"
    };

    /// <summary>True when the kind is a real gateway that can be selected.</summary>
    public static bool IsSelectable(this SmsProviderKind kind) => kind != SmsProviderKind.None;
}
