namespace KromicCommerce.Application.Abstractions.Sms;

/// <summary>
/// Single point of truth for "which SMS provider is active right now".
///
/// The configuration module guarantees at most one provider is enabled, so this factory
/// never has to choose between two live integrations. Callers must not resolve
/// <see cref="ISmsProvider"/> from DI directly — that bypasses the single-active-provider
/// rule and cannot report configuration problems.
/// </summary>
public interface ISmsProviderFactory
{
    /// <summary>
    /// Current runtime status, safe to expose on admin endpoints. Never contains secrets.
    /// </summary>
    SmsProviderStatus Status { get; }

    /// <summary>
    /// Returns the active provider, or <see cref="SmsProviderKind.None"/>'s no-op provider
    /// when SMS is not configured. Never throws for an unconfigured system.
    /// </summary>
    ISmsProvider Create();
}

/// <summary>
/// Configuration state of the SMS subsystem, projected without credentials.
/// </summary>
/// <param name="Enabled">Whether SMS delivery is switched on.</param>
/// <param name="Provider">The single active provider.</param>
/// <param name="IsConfigured">
/// True only when SMS is enabled, a supported provider is selected, and every credential
/// that provider requires is present.
/// </param>
/// <param name="MissingSettings">
/// Names of the configuration keys that still need to be supplied. Empty when configured.
/// </param>
public sealed record SmsProviderStatus(
    bool Enabled,
    SmsProviderKind Provider,
    bool IsConfigured,
    IReadOnlyList<string> MissingSettings)
{
    public static SmsProviderStatus Disabled { get; } =
        new(false, SmsProviderKind.None, false, []);

    /// <summary>True when a checkout must reject unverified phone numbers.</summary>
    public bool RequiresVerification => Enabled && IsConfigured;
}
