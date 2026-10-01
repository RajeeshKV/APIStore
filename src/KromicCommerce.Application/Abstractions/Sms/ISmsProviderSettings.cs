namespace KromicCommerce.Application.Abstractions.Sms;

/// <summary>
/// Resolves the effective SMS provider settings at send time.
/// </summary>
/// <remarks>
/// Settings come from two places, in this order:
/// <list type="number">
/// <item>the administrator's saved selection in the database, when one exists;</item>
/// <item>the application's configuration (environment variables).</item>
/// </list>
/// Configuration is the fallback rather than the source of truth so a deployment that has only
/// ever set environment variables keeps working untouched, while a store owner who configures a
/// gateway from the admin surface gets the settings they entered. Returning <c>null</c> means
/// "nothing saved in the database" — callers then use their configured options unchanged.
/// </remarks>
public interface ISmsProviderSettings
{
    /// <summary>
    /// The saved selection, or <c>null</c> when the administrator has never configured SMS.
    /// Settings are decrypted; callers must not log or return them.
    /// </summary>
    Task<SmsProviderSettingsSnapshot?> GetEffectiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The administrator's saved SMS configuration.
/// </summary>
/// <param name="Enabled">Whether SMS delivery is switched on.</param>
/// <param name="Provider">The single selected provider.</param>
/// <param name="Settings">Decrypted settings, keyed case-insensitively. May be empty.</param>
public sealed record SmsProviderSettingsSnapshot(
    bool Enabled,
    SmsProviderKind Provider,
    IReadOnlyDictionary<string, string> Settings)
{
    /// <summary>
    /// Reads a setting, ignoring case and surrounding whitespace, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Case is ignored here rather than relying on the dictionary's comparer: an admin form can
    /// send "apikey" where the adapters ask for "ApiKey", and that mismatch would otherwise
    /// depend on which layer built the dictionary.
    /// </remarks>
    public string? Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var wanted = key.Trim();

        if (Settings.TryGetValue(wanted, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();

        var match = Settings.FirstOrDefault(pair =>
            string.Equals(pair.Key.Trim(), wanted, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(match.Value) ? null : match.Value.Trim();
    }
}
