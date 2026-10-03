namespace KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Contracts.Admin;

/// <summary>
/// Everything the admin UI needs to render and validate one gateway's configuration.
/// </summary>
/// <remarks>
/// <para>
/// One table drives three things that must never drift apart:
/// </para>
/// <list type="number">
/// <item>what <c>GET /admin/integrations/sms/providers</c> returns for the UI to render,</item>
/// <item>which setting names <see cref="SmsSettingNames"/> accepts, and</item>
/// <item>the required-ness the save validator enforces.</item>
/// </list>
/// <para>
/// OTP generation, expiry, resend cooldown, and attempt limits are owned entirely by the
/// application — they are never exposed here or on the SMS configuration surface. Each provider
/// adapter hardcodes its own HTTP endpoint, method, request field names, and variable mappings.
/// </para>
/// </remarks>
public sealed record SmsProviderSchema(
    string Name,
    string Description,
    IReadOnlyList<string> RequiredSettings,
    IReadOnlyList<SmsProviderField> Settings,
    IReadOnlyList<string> Notes);

/// <summary>
/// The authoritative per-provider field catalogue.
/// </summary>
public static class SmsProviderFieldSchema
{
    private static SmsProviderField SecretKey(string key, string label, string help, string placeholder, int maxLength = 500) => new(
        Key: key,
        Label: label,
        Type: SmsFieldType.Secret,
        Required: true,
        Description: help,
        Placeholder: placeholder,
        MaxLength: maxLength);

    private static SmsProviderField TextField(string key, string label, string help, string placeholder, bool required = true, int maxLength = 200) => new(
        Key: key,
        Label: label,
        Type: SmsFieldType.Text,
        Required: required,
        Description: help,
        Placeholder: placeholder,
        MaxLength: maxLength);

    private static SmsProviderField TextareaField(string key, string label, string help, string placeholder, bool required = true, int maxLength = 1000) => new(
        Key: key,
        Label: label,
        Type: SmsFieldType.Textarea,
        Required: required,
        Description: help,
        Placeholder: placeholder,
        MaxLength: maxLength);

    /// <summary>
    /// Schema for <paramref name="provider"/>.
    /// </summary>
    public static SmsProviderSchema For(SmsProviderKind provider) => provider switch
    {
        SmsProviderKind.TwoFactor => TwoFactor,
        SmsProviderKind.Free2Sms => Free2Sms,
        SmsProviderKind.Twilio => Twilio,
        _ => None
    };

    private static readonly Lazy<IReadOnlyList<SmsProviderSchema>> _all = new(
        () => SmsProviderKinds.Selectable.Select(For).ToArray(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Schema for every selectable gateway, in display order.
    /// </summary>
    public static IReadOnlyList<SmsProviderSchema> All => _all.Value;

    // ---------------------------------------------------------------------
    // 2Factor
    // ---------------------------------------------------------------------
    // 2Factor's Manual OTP route sends the code this application generates. The template name is
    // the 2Factor-registered template name; both it and the code travel in the request path.
    // ---------------------------------------------------------------------
    private static SmsProviderSchema TwoFactor { get; } = new(
        Name: SmsProviderKind.TwoFactor.ToName(),
        Description: "API-key SMS gateway. Sends the verification code this application generates.",
        RequiredSettings: [SmsSettingNames.ApiKey, SmsSettingNames.TemplateName],
        Settings:
        [
            SecretKey(
                SmsSettingNames.ApiKey,
                "API Key",
                "The secret from your 2Factor account settings. 2Factor takes the key as part of the request URL, not as a header.",
                "Paste the key from your 2Factor account"),

            TextField(
                SmsSettingNames.TemplateName,
                "Template Name",
                "The exact name your OTP message template is registered under in the 2Factor portal. The generated code is appended to the request URL alongside this name.",
                "LOGIN_OTP")
        ],
        Notes:
        [
            "This adapter sends only the OTP code you generate; 2Factor does not generate or verify it.",
            "Register the template in your 2Factor portal and enter its name above."
        ]);

    // ---------------------------------------------------------------------
    // Free2SMS
    // ---------------------------------------------------------------------
    // Free2SMS has no OTP-specific endpoint. The adapter renders the message template with the
    // generated OTP and sends it through the standard /send endpoint. The template text must match
    // an approved DLT registration exactly.
    // ---------------------------------------------------------------------
    private static SmsProviderSchema Free2Sms { get; } = new(
        Name: SmsProviderKind.Free2Sms.ToName(),
        Description: "Bearer-token gateway with DLT template matching. India numbers only.",
        RequiredSettings: [SmsSettingNames.ApiKey, SmsSettingNames.SenderId, SmsSettingNames.MessageTemplate],
        Settings:
        [
            SecretKey(
                SmsSettingNames.ApiKey,
                "API Key",
                "The API key from your Free2SMS account. Sent as a bearer token.",
                "Paste the key from your Free2SMS account"),

            TextField(
                SmsSettingNames.SenderId,
                "Sender ID",
                "The DLT-approved 6-character header registered on your Free2SMS account.",
                "F2SMS"),

            TextareaField(
                SmsSettingNames.MessageTemplate,
                "OTP Message",
                "The SMS message body sent to customers. Use {{OTP}} as the placeholder for the verification code. The message must exactly match a DLT-approved registration in your Free2SMS account.",
                "Your verification code is {{OTP}}. It is valid for 5 minutes.")
        ],
        Notes:
        [
            "The {{OTP}} placeholder is replaced with the generated code at send time. No other placeholders are supported.",
            "Register this exact message as a DLT template in your Free2SMS dashboard before sending.",
            "The route is always 'otp' — it is not configurable."
        ]);

    // ---------------------------------------------------------------------
    // Twilio
    // ---------------------------------------------------------------------
    // Twilio uses Programmable Messaging (Messages API), not Verify. The adapter constructs the
    // message body with the generated OTP and sends it through the Messages endpoint.
    // ---------------------------------------------------------------------
    private static SmsProviderSchema Twilio { get; } = new(
        Name: SmsProviderKind.Twilio.ToName(),
        Description: "SMS delivery through Twilio Programmable Messaging.",
        RequiredSettings: [SmsSettingNames.AccountSid, SmsSettingNames.AuthToken, SmsSettingNames.FromNumber],
        Settings:
        [
            TextField(
                SmsSettingNames.AccountSid,
                "Account SID",
                "Your Twilio Account SID (starts with AC). Found in the Twilio Console.",
                "AC00000000000000000000000000000000"),

            SecretKey(
                SmsSettingNames.AuthToken,
                "Auth Token",
                "Your Twilio auth token. Never returned by any endpoint.",
                "Paste your auth token"),

            TextField(
                SmsSettingNames.FromNumber,
                "From Number",
                "The Twilio phone number to send from, in E.164 format.",
                "+15550000000")
        ],
        Notes:
        [
            "This adapter uses Twilio Programmable Messaging (Messages API), not Twilio Verify.",
            "The message body and OTP are constructed by this application; Twilio only delivers the SMS.",
            "Authentication uses HTTP Basic auth with AccountSid as the username and AuthToken as the password."
        ]);

    private static SmsProviderSchema None { get; } = new(
        Name: SmsProviderKind.None.ToName(),
        Description: "SMS delivery is not configured.",
        RequiredSettings: [],
        Settings: [],
        Notes: ["Select a provider to configure SMS delivery."]);

    /// <summary>
    /// Validates one submitted provider setting against its descriptor.
    /// </summary>
    /// <returns>An error message, or <c>null</c> when the value is acceptable.</returns>
    public static string? ValidateSetting(SmsProviderKind provider, string key, string? value)
    {
        var field = For(provider).Settings
            .FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

        if (field is null)
            return null;

        if (string.IsNullOrWhiteSpace(value))
            return field.Required ? $"{field.Label} is required." : null;

        if (field.MaxLength is { } max && value.Trim().Length > max)
            return $"{field.Label} must not exceed {max} characters.";

        return null;
    }
}
