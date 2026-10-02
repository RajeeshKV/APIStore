namespace KromicCommerce.Application.Abstractions.Sms;
using KromicCommerce.Contracts.Admin;

/// <summary>
/// Everything the admin UI needs to render and validate one gateway's configuration.
/// </summary>
/// <param name="Name">Canonical provider name, as written to configuration.</param>
/// <param name="Description">One-line summary.</param>
/// <param name="SupportsNativeOtp">
/// True when the gateway has a dedicated OTP endpoint. Drives whether the UI offers the delivery
/// mode choice at all.
/// </param>
/// <param name="RequiresTemplate">
/// True when the admin UI should show the template section at all. A gateway that can send
/// without any registration reports false, and the UI hides the whole section rather than
/// rendering inputs that have no effect.
/// </param>
/// <param name="RequiredSettings">Setting names that must be present when SMS is enabled.</param>
/// <param name="Settings">Provider settings, in render order.</param>
/// <param name="TemplateFields">Fields of the template form for this provider.</param>
/// <param name="Notes">Provider-specific guidance for the configuration screen.</param>
public sealed record SmsProviderSchema(
    string Name,
    string Description,
    bool SupportsNativeOtp,
    bool RequiresTemplate,
    IReadOnlyList<string> RequiredSettings,
    IReadOnlyList<SmsProviderField> Settings,
    IReadOnlyList<SmsProviderField> TemplateFields,
    IReadOnlyList<string> Notes);

/// <summary>
/// The authoritative per-provider field catalogue.
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
/// The vendor-meaning of <c>externalTemplateId</c> differs per gateway — a 2Factor template
/// <b>name</b>, a Twilio <c>TemplateSid</c>, a Free2SMS numeric DLT id. Describing it here is what
/// lets a single template form serve all three with the right label, placeholder and hint, and
/// what stops the front end from rendering "Template ID" for a value that is really a name.
/// </para>
/// </remarks>
public static class SmsProviderFieldSchema
{
    /// <summary>
    /// The vendor identifier as the UI must label it, per gateway.
    /// </summary>
    private const string TemplateNameKey = "externalTemplateId";

    /// <summary>The delivery modes offered wherever a gateway has a native OTP endpoint.</summary>
    public static readonly IReadOnlyList<string> DeliveryModeValues =
        ["Auto", "NativeOtp", "TransactionalTemplate"];

    private static SmsProviderField ApiKey(string help) => new(
        Key: SmsSettingNames.ApiKey,
        Label: "API key",
        Type: SmsFieldType.Secret,
        Required: true,
        Secret: true,
        HelpText: help,
        Placeholder: "Paste the key from your provider account",
        MaxLength: 500);

    private static SmsProviderField SenderId(
        string label, string help, string placeholder, bool required = false) => new(
        Key: SmsSettingNames.SenderId,
        Label: label,
        Type: SmsFieldType.Text,
        Required: required,
        HelpText: help,
        Placeholder: placeholder,
        MaxLength: 50);

    /// <summary>
    /// Schema for <paramref name="provider"/>.
    /// </summary>
    /// <remarks>
    /// Required-ness here is the backend's own enforcement, not a hint. A field marked
    /// <see cref="SmsProviderField.Required"/> is rejected by the save validator when SMS is
    /// enabled, and a field left out of <see cref="SmsProviderField.RequiredWhen"/> is never
    /// required — which is how Free2SMS ends up showing only its API key and sender ID while
    /// 2Factor additionally asks for its template name.
    /// </remarks>
    public static SmsProviderSchema For(SmsProviderKind provider) => provider switch
    {
        SmsProviderKind.TwoFactor => TwoFactor,
        SmsProviderKind.Free2Sms => Free2Sms,
        SmsProviderKind.Twilio => Twilio,
        _ => None
    };

    /// <summary>
    /// Schema for every selectable gateway, in display order.
    /// </summary>
    /// <remarks>
    /// Wrapped in <see cref="Lazy{T}"/> deliberately. As an eagerly initialised field this sat
    /// above the individual provider schemas in this type, so it ran during the static constructor
    /// before their initialisers had assigned — capturing an array of nulls, which surfaced as a
    /// provider list with null names. Deferring the projection to first access removes the
    /// dependence on declaration order entirely.
    /// </remarks>
    private static readonly Lazy<IReadOnlyList<SmsProviderSchema>> _all = new(
        () => SmsProviderKinds.Selectable.Select(For).ToArray(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <inheritdoc cref="_all"/>
    public static IReadOnlyList<SmsProviderSchema> All => _all.Value;

    private static SmsProviderSchema TwoFactor { get; } = new(
        Name: SmsProviderKind.TwoFactor.ToName(),
        Description: "API-key SMS gateway. Sends the verification code this application generates.",
        SupportsNativeOtp: true,
        RequiresTemplate: true,
        // Written out rather than delegating to SmsSettingNames.Required: that call would route
        // back through For() into this same static property, and the CLR does not re-enter a static
        // initializer, so the nested access would observe a half-built object.
        RequiredSettings: [SmsSettingNames.ApiKey],
        Settings:
        [
            ApiKey("2Factor 'secret' from Account settings. Sent as the X-API-Key header."),

            new SmsProviderField(
                Key: SmsSettingNames.DeliveryMode,
                Label: "Delivery mode",
                Type: SmsFieldType.Select,
                Required: false,
                Advanced: true,
                HelpText:
                    "Auto tries 2Factor's dedicated OTP endpoint first and falls back to the " +
                    "transactional template only when that route is unavailable.",
                AllowedValues: DeliveryModeValues,
                MaxLength: 32,
                DefaultValue: "Auto"),

            new SmsProviderField(
                Key: SmsSettingNames.OtpPath,
                Label: "OTP endpoint path",
                Type: SmsFieldType.Text,
                Required: false,
                Advanced: true,
                HelpText:
                    "Relative to the base URL. 2Factor has published more than one generation of " +
                    "this API, so correct this if your account's reference differs.",
                Placeholder: "/API/V1/OTP/SEND",
                FormatHint: "Must start with '/'",
                MaxLength: 200),

            new SmsProviderField(
                Key: SmsSettingNames.TransactionalPath,
                Label: "Transactional endpoint path",
                Type: SmsFieldType.Text,
                Required: false,
                Advanced: true,
                HelpText:
                    "Fallback route. Supports {apiKey} and {template}, where {template} receives " +
                    "the template name.",
                Placeholder: "/sms/{apiKey}/{template}",
                MaxLength: 200),

            new SmsProviderField(
                Key: SmsSettingNames.TemplateNameField,
                Label: "Template name field",
                Type: SmsFieldType.Text,
                Required: false,
                Advanced: true,
                HelpText:
                    "Request field carrying the template name. 2Factor's own pages disagree: " +
                    "template_name, template and templateName are all published.",
                Placeholder: "template_name",
                FormatHint: "One of template_name, template, templateName",
                MaxLength: 100),

            new SmsProviderField(
                Key: SmsSettingNames.OtpVariableName,
                Label: "OTP variable name",
                Type: SmsFieldType.Text,
                Required: false,
                Advanced: true,
                HelpText: "Template variable that receives the code. 2Factor addresses these positionally.",
                Placeholder: "var1",
                MaxLength: 50),

            new SmsProviderField(
                Key: SmsSettingNames.ExpiryVariableName,
                Label: "Expiry variable name",
                Type: SmsFieldType.Text,
                Required: false,
                Advanced: true,
                HelpText: "Template variable for the code lifetime. Only sent when the body uses {EXPIRY_MINUTES}.",
                Placeholder: "var2",
                MaxLength: 50),

            new SmsProviderField(
                Key: SmsSettingNames.ApiKeyHeader,
                Label: "API key header",
                Type: SmsFieldType.Text,
                Required: false,
                Advanced: true,
                HelpText: "Leave empty to send the key as apiKey in the request body instead.",
                Placeholder: "X-API-Key",
                MaxLength: 100),

            new SmsProviderField(
                Key: SmsSettingNames.Channel,
                Label: "Channel",
                Type: SmsFieldType.Select,
                Required: false,
                Advanced: true,
                HelpText: "How 2Factor delivers the message.",
                AllowedValues: ["SMS", "VOICE", "auto"],
                MaxLength: 10),

            SenderId("Sender ID", "Approved sender id registered on the 2Factor account.", "STORE"),
            BaseUrl()
        ],
        TemplateFields:
        [
            new SmsProviderField(
                Key: TemplateNameKey,
                Label: "Template name",
                Type: SmsFieldType.Text,
                Required: true,
                HelpText:
                    "The exact name your message template is registered under in the 2Factor " +
                    "portal. 2Factor matches on the name, not on a numeric ID.",
                Placeholder: "LOGIN_OTP",
                FormatHint: "The approved template name, exactly as registered",
                MaxLength: 100,
                RequiredWhen:
                    "Required whenever the transactional template route is used — that is, when " +
                    "Delivery mode is TransactionalTemplate, or when it is Auto and 2Factor " +
                    "reports the native OTP route is unavailable."),
            new SmsProviderField(
                Key: "body",
                Label: "Message body",
                Type: SmsFieldType.Textarea,
                Required: false,
                HelpText:
                    "Optional. Leave empty to let 2Factor render the registered template. Use " +
                    "{OTP} and {EXPIRY_MINUTES} as placeholders.",
                Placeholder: "Your verification code is {OTP}.",
                MaxLength: 1000)
        ],
        Notes:
        [
            "The template name is sent on every request, so changing it takes effect immediately.",
            "Set Delivery mode to Transactional Template to keep code generation, expiry, " +
            "attempt limits and resend cooldown under this application's control.",
            "If your account's reference uses a different field or path name, correct it under " +
            "advanced — no rebuild is required."
        ]);

    private static SmsProviderSchema Free2Sms { get; } = new(
        Name: SmsProviderKind.Free2Sms.ToName(),
        Description: "Bearer-token gateway with DLT template matching. India numbers only.",
        SupportsNativeOtp: false,
        RequiresTemplate: true,
        // Inline for the same reason as TwoFactor — see the note there.
        RequiredSettings: [SmsSettingNames.ApiKey, SmsSettingNames.SenderId],
        Settings:
        [
            ApiKey("Free2SMS API key. Sent as a bearer token. Shown once at creation."),
            SenderId(
                "Sender ID",
                "DLT-approved 6-character header registered on the account. Free2SMS rejects an " +
                "unapproved sender before charging.",
                "F2SMS",
                // Required: Free2SMS has no way to infer the sender, and the configuration status
                // reports it missing alongside the API key.
                required: true),

            new SmsProviderField(
                Key: SmsSettingNames.Route,
                Label: "Route",
                Type: SmsFieldType.Text,
                Required: false,
                Advanced: true,
                HelpText: "'otp' selects the priority queue, which is correct for a sign-in code.",
                Placeholder: "otp",
                MaxLength: 20),

            BaseUrl()
        ],
        TemplateFields:
        [
            new SmsProviderField(
                Key: TemplateNameKey,
                Label: "DLT template ID",
                Type: SmsFieldType.Text,
                Required: false,
                HelpText:
                    "The numeric DLT content-template ID. Optional — when omitted, Free2SMS " +
                    "matches the message against your approved templates by content. Entering the " +
                    "ID is faster and avoids ambiguity.",
                Placeholder: "1207161234567890123",
                FormatHint: "Digits only — stored as text because it can exceed 64-bit range",
                MaxLength: 100),

            new SmsProviderField(
                Key: "body",
                Label: "Message body",
                Type: SmsFieldType.Textarea,
                Required: false,
                HelpText:
                    "Must match your approved DLT template exactly, or Free2SMS rejects it with " +
                    "TEMPLATE_MISMATCH before charging. Use {OTP} and {EXPIRY_MINUTES}. Leave " +
                    "empty to let Free2SMS match your registrations by content.",
                Placeholder: "Your verification code is {OTP}. It expires in {EXPIRY_MINUTES} minutes.",
                MaxLength: 1000)
        ],
        Notes:
        [
            "Free2SMS publishes no OTP-specific endpoint, so the transactional template is always " +
            "used and there is no delivery mode to choose.",
            "The message body must match the approved DLT registration exactly."
        ]);

    private static SmsProviderSchema Twilio { get; } = new(
        Name: SmsProviderKind.Twilio.ToName(),
        Description: "Hosted OTP service (Verify v2). International numbers.",
        SupportsNativeOtp: true,
        RequiresTemplate: true,
        // Inline for the same reason as TwoFactor — see the note there.
        RequiredSettings:
        [
            SmsSettingNames.AccountSid,
            SmsSettingNames.AuthToken,
            SmsSettingNames.ServiceSid
        ],
        Settings:
        [
            new SmsProviderField(
                Key: SmsSettingNames.AccountSid,
                Label: "Account SID",
                Type: SmsFieldType.Text,
                Required: true,
                HelpText: "Public identifier, safe to display.",
                Placeholder: "AC00000000000000000000000000000000",
                FormatHint: "Starts with AC",
                MaxLength: 34),

            new SmsProviderField(
                Key: SmsSettingNames.AuthToken,
                Label: "Auth token",
                Type: SmsFieldType.Secret,
                Required: true,
                Secret: true,
                HelpText: "Full account access. Never returned by any endpoint.",
                Placeholder: "Paste the auth token",
                MaxLength: 100),

            new SmsProviderField(
                Key: SmsSettingNames.ServiceSid,
                Label: "Verify Service SID",
                Type: SmsFieldType.Text,
                Required: true,
                HelpText: "Which pre-approved message set Twilio Verify uses.",
                Placeholder: "VA00000000000000000000000000000000",
                FormatHint: "Starts with VA",
                MaxLength: 34),

            new SmsProviderField(
                Key: SmsSettingNames.MessagingServiceSid,
                Label: "Messaging Service SID",
                Type: SmsFieldType.Text,
                Required: false,
                Advanced: true,
                HelpText: "Optional. Twilio requires this or a sender for some regions.",
                Placeholder: "MG00000000000000000000000000000000",
                FormatHint: "Starts with MG",
                MaxLength: 34),

            SenderId(
                "Sender number or ID",
                "Used only by the Programmable Messaging fallback, not by Verify.",
                "+15550000000"),

            new SmsProviderField(
                Key: SmsSettingNames.DeliveryMode,
                Label: "Delivery mode",
                Type: SmsFieldType.Select,
                Required: false,
                Advanced: true,
                HelpText:
                    "Verify IS Twilio's dedicated OTP endpoint. Transactional Template falls back " +
                    "to Programmable Messaging, which forfeits Verify's rate limiting.",
                AllowedValues: DeliveryModeValues,
                MaxLength: 32,
                DefaultValue: "NativeOtp"),

            new SmsProviderField(
                Key: SmsSettingNames.MessagingBaseUrl,
                Label: "Messaging base URL",
                Type: SmsFieldType.Text,
                Required: false,
                Advanced: true,
                HelpText: "Only used by the Programmable Messaging fallback. Separate from the Verify host.",
                Placeholder: "https://api.twilio.com",
                FormatHint: "Must be an absolute https URL",
                MaxLength: 300),

            new SmsProviderField(
                Key: SmsSettingNames.MessagingPath,
                Label: "Messaging endpoint path",
                Type: SmsFieldType.Text,
                Required: false,
                Advanced: true,
                HelpText: "Only used by the Programmable Messaging fallback. {accountSid} is filled in for you.",
                Placeholder: "/2010-04-01/Accounts/{accountSid}/Messages.json",
                FormatHint: "Must start with '/'",
                MaxLength: 300),

            BaseUrl("https://verify.twilio.com")
        ],
        TemplateFields:
        [
            new SmsProviderField(
                Key: TemplateNameKey,
                Label: "Verify Template SID",
                Type: SmsFieldType.Text,
                Required: false,
                HelpText:
                    "Optional. Twilio falls back to the Service's default template, then to the " +
                    "Verify Default template, when this is empty. A value here wins over both.",
                Placeholder: "HJ00000000000000000000000000000000",
                FormatHint: "Twilio Verify template SID, starts with HJ",
                MaxLength: 100),

            new SmsProviderField(
                Key: "body",
                Label: "Message body",
                Type: SmsFieldType.Textarea,
                Required: false,
                HelpText:
                    "Optional. Placeholders here become TemplateCustomSubstitutions. A raw " +
                    "message body is never sent — Twilio rejects it with error 60243.",
                Placeholder: "Your verification code is {OTP}.",
                MaxLength: 1000)
        ],
        Notes:
        [
            "CustomCodeEnabled must be enabled on the Verify Service to send a code generated by " +
            "this application rather than one Twilio generates.",
            "Twilio template precedence: the Template SID set here, then the Service's default, " +
            "then the Verify Default template."
        ]);

    private static SmsProviderSchema None { get; } = new(
        Name: SmsProviderKind.None.ToName(),
        Description: "SMS delivery is not configured.",
        SupportsNativeOtp: false,
        RequiresTemplate: false,
        RequiredSettings: [],
        Settings: [],
        TemplateFields: [],
        Notes: ["Select a provider to configure SMS delivery."]);

    private static SmsProviderField BaseUrl(string example = "https://2factor.in") => new(
        Key: SmsSettingNames.BaseUrl,
        Label: "Base URL",
        Type: SmsFieldType.Text,
        Required: false,
        Advanced: true,
        HelpText: "Override only for a proxy, sandbox host or regional edge.",
        Placeholder: example,
        FormatHint: "Must be an absolute https URL",
        MaxLength: 300);

    /// <summary>
    /// Validates one submitted provider setting against its descriptor.
    /// </summary>
    /// <remarks>
    /// Applied on save so the backend enforces what the schema advertises. A descriptor the UI
    /// renders and validation that disagrees would let a client bypass the advertised rules —
    /// length limits and enumerated values in particular, which nothing checked before.
    /// </remarks>
    /// <returns>An error message, or <c>null</c> when the value is acceptable.</returns>
    public static string? ValidateSetting(SmsProviderKind provider, string key, string? value)
    {
        var field = For(provider).Settings
            .FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

        // Unknown keys are rejected by name elsewhere; nothing to check here.
        if (field is null)
            return null;

        if (string.IsNullOrWhiteSpace(value))
            return field.Required ? $"{field.Label} is required." : null;

        if (field.MaxLength is { } max && value.Trim().Length > max)
            return $"{field.Label} must not exceed {max} characters.";

        if (field.AllowedValues is { Count: > 0 } allowed
            && !allowed.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return $"{field.Label} must be one of: {string.Join(", ", allowed)}.";
        }

        // Only validated when the value is a real endpoint path; a blank field falls through above.
        if (string.Equals(field.Key, SmsSettingNames.OtpPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(field.Key, SmsSettingNames.TransactionalPath, StringComparison.OrdinalIgnoreCase))
        {
            if (!value.TrimStart().StartsWith('/'))
                return $"{field.Label} must start with '/'.";
        }

        return null;
    }

    /// <summary>
    /// The template field descriptor a gateway uses for its vendor registration identifier.
    /// </summary>
    public static SmsProviderField? TemplateReferenceField(SmsProviderKind provider) =>
        For(provider).TemplateFields
            .FirstOrDefault(f => string.Equals(f.Key, TemplateNameKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Validates one submitted template field against its descriptor for that gateway.
    /// </summary>
    /// <remarks>
    /// Same purpose as <see cref="ValidateSetting"/> but for the template form, and it enforces
    /// required-ness. 2Factor in particular was previously savable with a body and no template
    /// name, which then failed at send time with <c>TEMPLATE_NOT_CONFIGURED</c> — visible only in
    /// logs, days after the mistake. Rejecting it at the point of configuration is the whole
    /// reason the schema exists.
    /// </remarks>
    /// <returns>An error message, or <c>null</c> when the value is acceptable.</returns>
    public static string? ValidateTemplateField(SmsProviderKind provider, string key, string? value)
    {
        var field = For(provider).TemplateFields
            .FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

        if (field is null)
            return null;

        if (string.IsNullOrWhiteSpace(value))
            return field.Required ? $"{field.Label} is required." : null;

        if (field.MaxLength is { } max && value.Trim().Length > max)
            return $"{field.Label} must not exceed {max} characters.";

        // A 2Factor template name is free text, so only control characters and surrounding
        // whitespace are rejected. A Twilio Template SID and a Free2SMS DLT id are structured, so
        // a wrong shape is caught here rather than by a failed send.
        if (string.Equals(field.Key, TemplateNameKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(provider.ToName(), SmsProviderKind.Twilio.ToName(), StringComparison.Ordinal))
        {
            var trimmed = value.Trim();
            if (trimmed.Length != 34 || !trimmed.StartsWith("HJ", StringComparison.OrdinalIgnoreCase))
                return "Verify Template SID must start with 'HJ' and be 34 characters.";
        }

        return null;
    }

    /// <summary>
    /// Every validation problem with a submitted template, keyed by field so a form can highlight
    /// the offending inputs. Empty when the template is acceptable.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ValidateTemplate(
        SmsProviderKind provider, string? body, string? externalTemplateId)
    {
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (ValidateTemplateField(provider, "body", body) is { } bodyError)
            errors["body"] = bodyError;

        if (ValidateTemplateField(provider, TemplateNameKey, externalTemplateId) is { } templateError)
            errors[TemplateNameKey] = templateError;

        return errors;
    }
}
