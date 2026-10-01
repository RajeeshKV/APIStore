namespace KromicCommerce.Domain.Sms;

/// <summary>
/// The store's chosen SMS provider and its credentials, as configured by an administrator.
///
/// A single row. One provider is active at a time, so there is nothing to key this by and a
/// second row would be a bug rather than a configuration — <see cref="SingletonId"/> is a fixed
/// identifier and the write methods replace the selection outright rather than merging it, so
/// switching provider cannot leave the previous provider's credentials half-applied.
///
/// Credentials are stored individually encrypted, never as one protected blob: the status
/// endpoint has to report <em>which</em> settings are present without being able to read any of
/// them, and that distinction is only available if the keys are known independently of the
/// values.
/// </summary>
public sealed class SmsProviderConfig : AuditableEntity
{
    /// <summary>Fixed primary key — this entity is a singleton.</summary>
    public static readonly Guid SingletonId = new("6f1b6c40-1f2a-4f5e-9c3d-2a7e5b8d9f10");

    private SmsProviderConfig() { } // EF constructor

    private SmsProviderConfig(
        bool enabled, SmsProviderKind provider, IReadOnlyDictionary<string, string> encryptedSettings)
    {
        Enabled = enabled;
        Provider = provider.ToName();
        EncryptedSettings = Serialize(encryptedSettings);
    }

    /// <summary>Whether SMS delivery is switched on for the selected provider.</summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// Canonical name of the selected provider, or <c>None</c>. Always one of
    /// <see cref="SmsProviderKinds.Selectable"/> or "None" — an unsupported name cannot be
    /// written, because the application validates before calling <see cref="Configure"/>.
    /// </summary>
    public string Provider { get; private set; } = "None";

    /// <summary>
    /// Setting name → protected value. Never decrypted except at the point of use, and never
    /// returned by any API.
    /// </summary>
    public string EncryptedSettings { get; private set; } = "{}";

    /// <summary>The selected provider, or <see cref="SmsProviderKind.None"/>.</summary>
    public SmsProviderKind Kind => SmsProviderKinds.Parse(Provider) ?? SmsProviderKind.None;

    public static SmsProviderConfig Create(
        bool enabled, SmsProviderKind provider, IReadOnlyDictionary<string, string> encryptedSettings)
    {
        ArgumentNullException.ThrowIfNull(encryptedSettings);
        return new SmsProviderConfig(enabled, provider, encryptedSettings);
    }

    /// <summary>
    /// Replaces the selection and its credentials. Settings absent from
    /// <paramref name="encryptedSettings"/> are dropped rather than retained, so removing a key
    /// from the admin form actually removes the credential.
    /// </summary>
    public void Configure(
        bool enabled, SmsProviderKind provider, IReadOnlyDictionary<string, string> encryptedSettings)
    {
        ArgumentNullException.ThrowIfNull(encryptedSettings);

        Enabled = enabled;
        Provider = provider.ToName();
        EncryptedSettings = Serialize(encryptedSettings);
    }

    /// <summary>
    /// Serialises the setting map. Serialisation lives here, beside the field, so the shape of
    /// <see cref="EncryptedSettings"/> cannot drift between the write path and whatever reads it
    /// back, and so a setting name that would change the JSON structure cannot be stored.
    /// </summary>
    private static string Serialize(IReadOnlyDictionary<string, string> settings)
    {
        var flat = settings
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        return System.Text.Json.JsonSerializer.Serialize(flat);
    }
}
