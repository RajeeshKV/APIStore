using KromicCommerce.Application.Abstractions.Sms;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Shared stand-ins for the SMS seams so tests can state their intent without restating the
/// whole configuration surface.
/// </summary>
internal static class SmsTestDoubles
{
    /// <summary>A factory whose status reports SMS as enabled and fully configured.</summary>
    public static ISmsProviderFactory Configured(SmsProviderKind kind = SmsProviderKind.Twilio)
        => new StubFactory(new SmsProviderStatus(
            Enabled: true,
            Provider: kind,
            IsConfigured: true,
            MissingSettings: []));

    /// <summary>
    /// A factory reporting SMS as enabled and fully configured, handing out one shared provider.
    /// Sharing matters for concurrency tests: every request resolves its own provider instance in
    /// production, but the assertion needs a single place to count what was actually sent.
    /// </summary>
    public static ISmsProviderFactory ConfiguredWith(ISmsProvider provider)
        => new StubFactory(new SmsProviderStatus(
            Enabled: true,
            Provider: provider.Kind,
            IsConfigured: true,
            MissingSettings: []), provider);

    /// <summary>A factory whose status reports SMS as switched off.</summary>
    public static ISmsProviderFactory NotConfigured()
        => new StubFactory(new SmsProviderStatus(
            Enabled: false,
            Provider: SmsProviderKind.None,
            IsConfigured: false,
            MissingSettings: ["Sms:Enabled"]));

    /// <summary>A factory reporting a specific set of unresolved configuration keys.</summary>
    public static ISmsProviderFactory PartiallyConfigured(params string[] missing)
        => new StubFactory(new SmsProviderStatus(
            Enabled: true,
            Provider: SmsProviderKind.Twilio,
            IsConfigured: false,
            MissingSettings: missing));

    /// <summary>
    /// A template store that returns the supplied template for every provider, or none at all.
    /// Adapters are the only callers, and each one asks for its own kind, so a single value is
    /// enough to exercise both the "template configured" and "no template configured" paths.
    /// </summary>
    public static ISmsTemplateStore Templates(SmsTemplateSnapshot? template = null)
        => new StubTemplateStore(template);

    /// <summary>A template store returning a body-only template for <paramref name="provider"/>.</summary>
    public static ISmsTemplateStore BodyTemplate(
        SmsProviderKind provider, string body, string? externalTemplateId = null)
        => new StubTemplateStore(new SmsTemplateSnapshot(provider, "Test template", externalTemplateId, body));

    /// <summary>
    /// No saved administrator selection, so adapters fall back to their configured options. This
    /// is the state of a deployment that only ever set environment variables.
    /// </summary>
    public static SmsProviderSettingsSnapshot? NoSavedSettings() => null;

    /// <summary>An administrator has saved a selection and these settings.</summary>
    public static SmsProviderSettingsSnapshot SavedSettings(
        SmsProviderKind provider, bool enabled = true, params (string Key, string Value)[] settings)
        => new(enabled, provider, settings.ToDictionary(
            s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase));

    /// <summary>An administrator has saved a selection with no settings at all.</summary>
    public static SmsProviderSettingsSnapshot SavedWithoutSettings(
        SmsProviderKind provider, bool enabled = true)
        => new(enabled, provider, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// A settings source backed by a fixed snapshot, or by nothing saved at all.
    /// </summary>
    public static ISmsProviderSettings ProviderSettings(SmsProviderSettingsSnapshot? snapshot)
        => new StubProviderSettings(snapshot);

    private sealed class StubProviderSettings(SmsProviderSettingsSnapshot? snapshot) : ISmsProviderSettings
    {
        public Task<SmsProviderSettingsSnapshot?> GetEffectiveAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(snapshot);
    }

    private sealed class StubTemplateStore(SmsTemplateSnapshot? template) : ISmsTemplateStore
    {
        public Task<SmsTemplateSnapshot?> GetActiveAsync(
            SmsProviderKind provider, CancellationToken cancellationToken = default)
            => Task.FromResult(template is null || template.Provider == provider ? template : null);
    }

    public static IOptions<SmsPolicyOptions> Policy(
        bool requireVerifiedPhoneAtCheckout = false,
        int expiryMinutes = 10,
        int resendCooldownSeconds = 60,
        int maxAttempts = 5,
        int length = 6)
        => Options.Create(new SmsPolicyOptions
        {
            RequireVerifiedPhoneAtCheckout = requireVerifiedPhoneAtCheckout,
            ExpiryMinutes = expiryMinutes,
            ResendCooldownSeconds = resendCooldownSeconds,
            MaxAttempts = maxAttempts,
            Length = length
        });

    /// <summary>Records what was sent so tests can assert on it.</summary>
    internal sealed class RecordingProvider(
        SmsSendResult result, SmsProviderKind kind = SmsProviderKind.Twilio) : ISmsProvider
    {
        private readonly object _gate = new();
        private readonly List<(string PhoneNumber, string Otp)> _sent = [];

        public string ProviderName => "Test";

        public SmsProviderKind Kind { get; } = kind;

        /// <summary>Guarded because a resend race test drives sends from several threads at once.</summary>
        public IReadOnlyList<(string PhoneNumber, string Otp)> Sent
        {
            get { lock (_gate) { return _sent.ToArray(); } }
        }

        public Task<SmsSendResult> SendOtpAsync(
            string phoneNumber, string otp, CancellationToken cancellationToken = default)
        {
            lock (_gate) { _sent.Add((phoneNumber, otp)); }
            return Task.FromResult(result);
        }
    }

    private sealed class StubFactory(SmsProviderStatus status, ISmsProvider? provider = null) : ISmsProviderFactory
    {
        private readonly ISmsProvider _provider = provider ?? (status.IsConfigured
            ? new RecordingProvider(new SmsSendResult(true, "msg-1", null, null, false))
            : new RecordingProvider(
                new SmsSendResult(false, null, "SMS_NOT_CONFIGURED", "not configured", false),
                SmsProviderKind.None));

        public SmsProviderStatus Status { get; } = status;

        public Task<SmsProviderStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Status);

        public Task<ISmsProvider> CreateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_provider);
    }
}
