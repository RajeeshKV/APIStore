namespace KromicCommerce.Application.Abstractions.Sms;

/// <summary>
/// Resolves the single active SMS provider and its effective settings.
/// </summary>
/// <remarks>
/// <para>
/// <b>Single source of truth.</b> The administrator's saved selection in the database
/// (<c>sms_provider_configs</c>) is authoritative: it decides both which provider is active and
/// whether SMS is switched on. Deployment configuration (<c>Sms:*</c> environment variables) is
/// used only when no row has been saved yet — the bootstrap that lets a fresh deployment work
/// before anyone visits the admin screen.
/// </para>
/// <para>
/// The rule is total and has no field-level disagreement: <i>a saved row always wins</i>. Once an
/// administrator saves any SMS configuration, changing an environment variable no longer changes
/// which provider is active or whether SMS counts as configured. Without that rule the two
/// sources could contradict each other — the admin screen showing Twilio while Free2SMS delivered
/// the last code — and nothing would say so.
/// </para>
/// <para>
/// Individual credential values may still fall back to configuration individually, so a
/// partially completed admin form stays usable. That is a credential default, not a second source
/// of truth for <em>which</em> provider is active.
/// </para>
/// <para>
/// Methods are asynchronous because resolution reads the database. That is also what makes admin
/// changes take effect on the next request with no restart and no cache invalidation.
/// </para>
/// </remarks>
public interface ISmsProviderFactory
{
    /// <summary>
    /// The current status: which provider is active, whether SMS counts as configured, and which
    /// settings are still missing.
    /// </summary>
    Task<SmsProviderStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The active provider. Returns a non-operational no-op stand-in when SMS is disabled or
    /// incompletely configured — never throws and never <c>null</c>, so a configuration problem
    /// cannot take the OTP path down.
    /// </summary>
    Task<ISmsProvider> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A snapshot of the resolved SMS configuration.
/// </summary>
/// <param name="Enabled">Whether SMS delivery is switched on.</param>
/// <param name="Provider">The single active provider, or <c>None</c>.</param>
/// <param name="IsConfigured">True when SMS is enabled and every required setting is present.</param>
/// <param name="MissingSettings">
/// Configuration paths still required before SMS can send, e.g. <c>Sms:Twilio:FromNumber</c>.
/// Names only — never values.
/// </param>
public sealed record SmsProviderStatus(
    bool Enabled,
    SmsProviderKind Provider,
    bool IsConfigured,
    IReadOnlyList<string> MissingSettings)
{
    /// <summary>
    /// Whether checkout should demand a verified phone.
    ///
    /// False whenever SMS cannot actually deliver, even if policy asks for verification —
    /// otherwise a configuration gap would lock every customer out of checkout.
    /// </summary>
    public bool RequiresVerification => Enabled && IsConfigured;
}