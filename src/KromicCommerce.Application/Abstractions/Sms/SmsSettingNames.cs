namespace KromicCommerce.Application.Abstractions.Sms;

/// <summary>
/// The setting names each SMS provider accepts.
/// </summary>
/// <remarks>
/// <para>
/// Names match the corresponding <c>*Options</c> property on the Infrastructure options class
/// for that provider, and are used as the wire keys for <c>PUT /admin/integrations/sms</c>.
/// </para>
/// <para>
/// Derived from <see cref="SmsProviderFieldSchema"/>: a field rendered by the admin form is by
/// construction accepted on save, and a field accepted on save is rendered by the admin form.
/// Previously these tables could drift, so a setting could exist in documentation while the
/// validator rejected it.
/// </para>
/// </remarks>
public static class SmsSettingNames
{
    // ---------------------------------------------------------------------
    // Key names. These match the corresponding options-class property names and
    // are used as the wire keys for PUT /admin/integrations/sms.
    // Only the fields an administrator actually configures are listed here;
    // provider HTTP implementation details (endpoints, variable names, headers)
    // are hardcoded in the adapters and never stored.
    // ---------------------------------------------------------------------

    /// <summary>API key / token. Present for every gateway that uses one.</summary>
    public const string ApiKey = "ApiKey";

    /// <summary>2Factor template name registered in the 2Factor portal.</summary>
    public const string TemplateName = "TemplateName";

    /// <summary>Free2SMS DLT-approved 6-character sender header.</summary>
    public const string SenderId = "SenderId";

    /// <summary>Free2SMS OTP message template containing {{OTP}}.</summary>
    public const string MessageTemplate = "MessageTemplate";

    /// <summary>Twilio account SID (AC…).</summary>
    public const string AccountSid = "AccountSid";

    /// <summary>Twilio auth token.</summary>
    public const string AuthToken = "AuthToken";

    /// <summary>Twilio sender phone number for Programmable Messaging.</summary>
    public const string FromNumber = "FromNumber";

    /// <summary>Settings without which the provider cannot send. Required when SMS is enabled.</summary>
    public static IReadOnlyList<string> Required(SmsProviderKind provider)
        => SmsProviderFieldSchema.For(provider).RequiredSettings;

    /// <summary>Every setting name accepted for <paramref name="provider"/>, required included.</summary>
    public static IReadOnlyList<string> All(SmsProviderKind provider)
        => SmsProviderFieldSchema.For(provider).Settings.Select(f => f.Key).ToList();

    /// <summary>True when <paramref name="name"/> is a real setting for the provider, ignoring case.</summary>
    public static bool IsKnown(SmsProviderKind provider, string? name)
        => !string.IsNullOrWhiteSpace(name) &&
           All(provider).Any(k => string.Equals(k.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
}
