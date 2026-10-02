namespace KromicCommerce.Contracts.Admin;

/// <summary>How the admin UI should render a configuration field.</summary>
public enum SmsFieldType
{
    /// <summary>Single-line free text.</summary>
    Text = 0,

    /// <summary>Password-style input; the value is never returned by any endpoint.</summary>
    Secret = 1,

    /// <summary>Multi-line free text.</summary>
    Textarea = 2,

    /// <summary>Integer input.</summary>
    Number = 3,

    /// <summary>Fixed choice; see <see cref="SmsProviderField.AllowedValues"/>.</summary>
    Select = 4
}

/// <summary>
/// One configurable input for a gateway, described well enough that the admin UI does not have to
/// know anything about the provider.
/// </summary>
/// <remarks>
/// This is the mechanism that makes the backend the source of truth for the configuration form.
/// The per-provider fields previously existed only as prose in a Markdown document, so a front end
/// had to hard-code which inputs to render for which gateway, which of them mattered, and what
/// they meant — and the vendor meaning of one field differs per gateway: a 2Factor template
/// <b>name</b>, a Twilio <c>TemplateSid</c>, a Free2SMS numeric DLT id. Serving the descriptors
/// from the same table that validates the save means the rendered form and the enforced rules
/// cannot drift apart.
/// </remarks>
/// <param name="Key">
/// The property to send. For a provider setting this is the key that
/// <c>PUT /admin/integrations/sms</c> accepts; for a template field it is the template request
/// property, e.g. <c>externalTemplateId</c>.
/// </param>
/// <param name="Label">Form label.</param>
/// <param name="Type">How to render.</param>
/// <param name="Required">
/// True when the write is rejected without it. A conditional requirement is expressed through
/// <paramref name="RequiredWhen"/> instead, so the backend never marks a field unconditionally
/// required when it is only required for one configuration.
/// </param>
/// <param name="Secret">True for credentials: write-only, never returned.</param>
/// <param name="Advanced">
/// True for route and tuning knobs an administrator rarely changes. A UI may collapse these
/// behind a disclosure; they remain fully configurable and fully validated.
/// </param>
/// <param name="HelpText">Guidance shown under the input.</param>
/// <param name="Placeholder">Example value to show in an empty input.</param>
/// <param name="FormatHint">The expected shape in words, e.g. <c>Starts with AC</c>.</param>
/// <param name="MaxLength">Server-enforced maximum, mirrored so the UI can pre-validate.</param>
/// <param name="AllowedValues">Permitted values when <paramref name="Type"/> is Select.</param>
/// <param name="RequiredWhen">Human-readable condition under which this becomes required.</param>
/// <param name="DefaultValue">
/// The value the backend applies when the field is left unset, so the UI can show the effective
/// setting instead of an empty box that looks unconfigured.
/// </param>
public sealed record SmsProviderField(
    string Key,
    string Label,
    SmsFieldType Type,
    bool Required,
    bool Secret = false,
    bool Advanced = false,
    string? HelpText = null,
    string? Placeholder = null,
    string? FormatHint = null,
    int? MaxLength = null,
    IReadOnlyList<string>? AllowedValues = null,
    string? RequiredWhen = null,
    string? DefaultValue = null);

/// <summary>
/// A gateway the administrator may select, together with everything needed to render and validate
/// its configuration form.
/// </summary>
/// <remarks>
/// <para>
/// The field descriptors are the backend's source of truth, not documentation. The same table
/// validates the save, so a client cannot submit something the form would not have allowed, and
/// the front end needs no hard-coded knowledge of which gateway takes which inputs.
/// </para>
/// <para>
/// <c>Name</c> and <c>Description</c> are retained, and positional construction of this record is
/// unchanged, so existing clients keep working.
/// </para>
/// </remarks>
/// <param name="Name">Canonical provider name, as written to configuration.</param>
/// <param name="Description">What the gateway requires, so the admin screen can explain prerequisites.</param>
/// <param name="SupportsNativeOtp">True when the gateway has a dedicated OTP endpoint.</param>
/// <param name="RequiresTemplate">
/// True when the admin UI should render the template section. False means the whole section is
/// hidden, because the gateway can deliver without any registered template.
/// </param>
/// <param name="RequiredSettings">Setting names that must be present when SMS is enabled.</param>
/// <param name="Settings">Provider settings to render, in order.</param>
/// <param name="TemplateFields">Fields of the template form for this provider.</param>
/// <param name="Notes">Provider-specific guidance for the configuration screen.</param>
public sealed record SmsProviderOptionResponse(
    string Name,
    string Description,
    bool SupportsNativeOtp,
    bool RequiresTemplate,
    IReadOnlyList<string> RequiredSettings,
    IReadOnlyList<SmsProviderField> Settings,
    IReadOnlyList<SmsProviderField> TemplateFields,
    IReadOnlyList<string> Notes);

/// <summary>
/// An SMS message template as returned on the admin surface.
/// </summary>
/// <param name="Id">Template identifier.</param>
/// <param name="Provider">The gateway this template is registered with.</param>
/// <param name="Name">Admin-facing label.</param>
/// <param name="Body">The message body containing placeholder tokens, or empty.</param>
/// <param name="ExternalTemplateId">Vendor-side template registration identifier, or null.</param>
/// <param name="IsActive">True when this template is the one used for OTP delivery.</param>
/// <param name="UpdatedAtUtc">Last modification time.</param>
public sealed record SmsTemplateResponse(
    Guid Id,
    string Provider,
    string Name,
    string Body,
    string? ExternalTemplateId,
    bool IsActive,
    DateTime UpdatedAtUtc);

/// <summary>Creates a template. The provider cannot be changed afterwards.</summary>
public sealed record CreateSmsTemplateRequest(
    string Provider,
    string Name,
    string? Body,
    string? ExternalTemplateId,
    bool IsActive = true);

/// <summary>Edits a template. <see cref="IsActive"/> promotes or demotes it for the provider.</summary>
public sealed record UpdateSmsTemplateRequest(
    string Name,
    string? Body,
    string? ExternalTemplateId,
    bool IsActive);
