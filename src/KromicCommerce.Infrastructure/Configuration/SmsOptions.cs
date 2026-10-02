namespace KromicCommerce.Infrastructure.Configuration;

/// <summary>
/// Root of the SMS configuration module.
/// </summary>
/// <remarks>
/// <para>
/// SMS is OPTIONAL — the application must start and serve requests without it configured.
/// When <see cref="Enabled"/> is false, or <see cref="Provider"/> is <c>None</c>, or the
/// selected provider's credentials are missing, the system runs in a clearly reported
/// "not configured" state: OTP delivery is refused with a business error and no other
/// authentication method is affected.
/// </para>
/// <para>
/// Exactly one provider is active. Credentials for the other providers may be present in
/// configuration but are never read — only <see cref="Provider"/> is resolved.
/// </para>
/// <para>
/// OTP policy (expiry, resend cooldown, attempt limits, code length) is fixed application
/// defaults defined in <see cref="KromicCommerce.Application.Options.SmsOtpDefaults"/>.
/// It is not configurable from the SMS integration surface.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Sms:
///   Enabled: true
///   Provider: Twilio          # 2Factor | Free2SMS | Twilio
///   RequireVerifiedPhoneAtCheckout: true
///   TwoFactor: { ApiKey: "...", TemplateName: "LOGIN_OTP" }
///   Free2Sms: { ApiKey: "...", SenderId: "F2SMS", MessageTemplate: "Your code is {{OTP}}" }
///   Twilio:   { AccountSid: "AC...", AuthToken: "...", FromNumber: "+1..." }
/// </code>
/// </example>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";

    /// <summary>Master switch. When false no SMS is ever sent.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The single active provider. Only the names returned by
    /// <see cref="SmsProviderKinds.Selectable"/> are recognised; anything else is reported as
    /// <see cref="SmsProviderKind.None"/> rather than being guessed at.
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>
    /// When true, checkout rejects any customer whose phone is not verified, and rejects
    /// addresses carrying a phone other than the verified one. Only meaningful while SMS is
    /// configured, because verification requires delivery.
    /// </summary>
    public bool RequireVerifiedPhoneAtCheckout { get; init; }

    public TwoFactorOptions TwoFactor { get; init; } = new();
    public Free2SmsOptions Free2Sms { get; init; } = new();
    public TwilioOptions Twilio { get; init; } = new();

    /// <summary>The selected provider, or <see cref="SmsProviderKind.None"/> when unset/unrecognised.</summary>
    public SmsProviderKind SelectedProvider =>
        SmsProviderKinds.Parse(Provider) ?? SmsProviderKind.None;

    /// <summary>
    /// The configuration keys still missing for <see cref="SelectedProvider"/> to be usable.
    /// Empty means the selection is complete.
    /// </summary>
    public IReadOnlyList<string> GetMissingSettings()
    {
        var missing = new List<string>();

        if (!Enabled)
            return ["Sms:Enabled"];

        switch (SelectedProvider)
        {
            case SmsProviderKind.TwoFactor:
                if (string.IsNullOrWhiteSpace(TwoFactor.ApiKey)) missing.Add("Sms:TwoFactor:ApiKey");
                if (string.IsNullOrWhiteSpace(TwoFactor.TemplateName)) missing.Add("Sms:TwoFactor:TemplateName");
                break;

            case SmsProviderKind.Free2Sms:
                if (string.IsNullOrWhiteSpace(Free2Sms.ApiKey)) missing.Add("Sms:Free2Sms:ApiKey");
                if (string.IsNullOrWhiteSpace(Free2Sms.SenderId)) missing.Add("Sms:Free2Sms:SenderId");
                if (string.IsNullOrWhiteSpace(Free2Sms.MessageTemplate)) missing.Add("Sms:Free2Sms:MessageTemplate");
                break;

            case SmsProviderKind.Twilio:
                if (string.IsNullOrWhiteSpace(Twilio.AccountSid)) missing.Add("Sms:Twilio:AccountSid");
                if (string.IsNullOrWhiteSpace(Twilio.AuthToken)) missing.Add("Sms:Twilio:AuthToken");
                if (string.IsNullOrWhiteSpace(Twilio.FromNumber)) missing.Add("Sms:Twilio:FromNumber");
                break;

            case SmsProviderKind.None:
            default:
                missing.Add("Sms:Provider");
                break;
        }

        return missing;
    }

    /// <summary>True when SMS is enabled and the selected provider has everything it needs.</summary>
    public bool IsConfigured => GetMissingSettings().Count == 0;
}
