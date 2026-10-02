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
/// <para>
/// This is the mechanism that makes the backend the source of truth for the configuration form.
/// The per-provider fields are served from the same table that validates the save, so a client
/// cannot submit something the form would not have allowed, and the front end needs no hard-coded
/// knowledge of which gateway takes which inputs.
/// </para>
/// <para>
/// Only UI/configuration information is exposed — never provider HTTP implementation details such
/// as endpoint paths, request field names, variable names, or retry settings. Those are hardcoded
/// in the provider adapters.
/// </para>
/// </remarks>
/// <param name="Key">The property name accepted by <c>PUT /admin/integrations/sms</c>.</param>
/// <param name="Label">Form label.</param>
/// <param name="Type">How to render the input.</param>
/// <param name="Required">True when the write is rejected without it.</param>
/// <param name="Description">Help text shown under or beside the input.</param>
/// <param name="Placeholder">Example value to show in an empty input.</param>
/// <param name="MaxLength">Server-enforced maximum, mirrored so the UI can pre-validate.</param>
/// <param name="AllowedValues">Permitted values when <paramref name="Type"/> is Select.</param>
public sealed record SmsProviderField(
    string Key,
    string Label,
    SmsFieldType Type,
    bool Required,
    string? Description = null,
    string? Placeholder = null,
    int? MaxLength = null,
    IReadOnlyList<string>? AllowedValues = null);

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
/// </remarks>
/// <param name="Name">Canonical provider name, as written to configuration.</param>
/// <param name="Description">What the gateway requires, so the admin screen can explain prerequisites.</param>
/// <param name="RequiredSettings">Setting names that must be present when SMS is enabled.</param>
/// <param name="Settings">Provider settings to render, in order.</param>
/// <param name="Notes">Provider-specific guidance for the configuration screen.</param>
public sealed record SmsProviderOptionResponse(
    string Name,
    string Description,
    IReadOnlyList<string> RequiredSettings,
    IReadOnlyList<SmsProviderField> Settings,
    IReadOnlyList<string> Notes);
