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

    /// <summary>A factory whose status reports SMS as switched off.</summary>
    public static ISmsProviderFactory NotConfigured()
        => new StubFactory(SmsProviderStatus.Disabled);

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
    /// No saved administrator selection, so adapters use their configured options. This is the
    /// state of a deployment that only ever set environment variables.
    /// </summary>
    public static ISmsProviderSettings NoSavedSettings() => new StubProviderSettings(null);

    /// <summary>An administrator has saved a selection and these settings.</summary>
    public static ISmsProviderSettings SavedSettings(
        SmsProviderKind provider, bool enabled = true, params (string Key, string Value)[] settings)
        => new StubProviderSettings(
            new SmsProviderSettingsSnapshot(enabled, provider, settings.ToDictionary(
                s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase)));

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
        public string ProviderName => "Test";

        public SmsProviderKind Kind { get; } = kind;

        public List<(string PhoneNumber, string Otp)> Sent { get; } = [];

        public Task<SmsSendResult> SendOtpAsync(
            string phoneNumber, string otp, CancellationToken cancellationToken = default)
        {
            Sent.Add((phoneNumber, otp));
            return Task.FromResult(result);
        }
    }

    private sealed class StubFactory(SmsProviderStatus status) : ISmsProviderFactory
    {
        public SmsProviderStatus Status { get; } = status;

        public ISmsProvider Create()
            => Status.IsConfigured
                ? new RecordingProvider(new SmsSendResult(true, "msg-1", null, null, false))
                : new RecordingProvider(
                    new SmsSendResult(false, null, "SMS_NOT_CONFIGURED", "not configured", false),
                    SmsProviderKind.None);
    }
}
