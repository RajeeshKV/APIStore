using KromicCommerce.Application.Abstractions.Sms;

namespace KromicCommerce.Infrastructure.Sms;

/// <summary>
/// Merges the administrator's saved SMS settings over the values from configuration.
/// </summary>
/// <remarks>
/// The saved value wins when present so the admin surface is authoritative once anything has
/// been saved there; configuration still supplies anything the admin form left blank, which
/// keeps a half-completed setup usable and lets a deployment be migrated without re-entering
/// every key.
/// </remarks>
internal static class SmsSettings
{
    /// <summary>Saved value if present, else the configured value, else null.</summary>
    public static string? Resolve(SmsProviderSettingsSnapshot? saved, string key, string? configured)
        => saved?.Get(key) ?? (string.IsNullOrWhiteSpace(configured) ? null : configured.Trim());

    /// <summary>
    /// True when the saved settings belong to <paramref name="kind"/>. <c>null</c> — nothing
    /// saved — belongs to every provider.
    /// </summary>
    /// <remarks>
    /// Each adapter checks this before using anything it was handed. Without it, switching
    /// provider in the admin screen would leave the previous provider's credentials readable to
    /// the adapter it had just replaced, which is the same class of bug as reading the inactive
    /// provider's configuration.
    /// </remarks>
    public static bool AppliesTo(SmsProviderSettingsSnapshot? saved, SmsProviderKind kind)
        => saved is null || saved.Provider == kind;
}
