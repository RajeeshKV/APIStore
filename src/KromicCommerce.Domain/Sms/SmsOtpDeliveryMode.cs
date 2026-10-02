namespace KromicCommerce.Domain.Sms;

/// <summary>
/// How an OTP is handed to the gateway.
/// </summary>
/// <remarks>
/// <para>
/// Each provider can expose more than one way to deliver a code, and they differ in a way that
/// matters to the customer: <see cref="NativeOtp"/> moves code generation and validation to the
/// vendor, while <see cref="TransactionalTemplate"/> keeps this application authoritative.
/// </para>
/// <para>
/// The difference is not cosmetic. With <see cref="NativeOtp"/> the vendor chooses the code
/// length, the expiry, and how many wrong attempts are tolerated, and the vendor's Verification
/// Check API becomes the only way to validate it. With <see cref="TransactionalTemplate"/> this
/// application generates the code, stores only its hash, applies its own expiry, attempt limits
/// and resend cooldown, and every gateway behaves identically. That uniformity is the reason
/// <see cref="SmsProviderKind"/> is selectable at runtime at all.
/// </para>
/// <para>
/// <see cref="Auto"/> is the default because it is the only mode that is always safe to turn on:
/// it prefers the provider's dedicated OTP endpoint, and falls back to the transactional template
/// whenever the native route is unavailable or unsupported.
/// </para>
/// </remarks>
public enum SmsOtpDeliveryMode
{
    /// <summary>
    /// Default. Use the provider's dedicated OTP endpoint when it has one, and fall back to the
    /// transactional template route when it does not, or when the native call fails in a way that
    /// is safe to retry.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Always use the provider's dedicated OTP endpoint. The provider generates and verifies the
    /// code. Only meaningful for gateways that expose an OTP-specific API.
    /// </summary>
    NativeOtp = 1,

    /// <summary>
    /// Always send a code this application generated, through the provider's registered template.
    /// Keeps expiry, hashing, attempt limits and cooldown under this application's control.
    /// </summary>
    TransactionalTemplate = 2
}

/// <summary>Helpers over <see cref="SmsOtpDeliveryMode"/>.</summary>
public static class SmsOtpDeliveryModes
{
    /// <summary>
    /// Parses an administrator-supplied mode name. Returns <c>null</c> for anything
    /// unrecognised so callers can report a precise error instead of silently falling back to a
    /// mode the administrator did not ask for.
    /// </summary>
    public static SmsOtpDeliveryMode? Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var normalized = name.Trim().Replace(" ", string.Empty).Replace("_", string.Empty)
            .Replace("-", string.Empty).ToLowerInvariant();

        return normalized switch
        {
            "auto" or "default" or "automatic" => SmsOtpDeliveryMode.Auto,
            "native" or "nativeotp" or "otpendpoint" or "hosted" => SmsOtpDeliveryMode.NativeOtp,
            "transactional" or "transactionaltemplate" or "template" or "customtemplate"
                => SmsOtpDeliveryMode.TransactionalTemplate,
            _ => null
        };
    }

    /// <summary>Canonical name written to configuration and shown on admin surfaces.</summary>
    public static string ToName(this SmsOtpDeliveryMode mode) => mode switch
    {
        SmsOtpDeliveryMode.NativeOtp => "NativeOtp",
        SmsOtpDeliveryMode.TransactionalTemplate => "TransactionalTemplate",
        _ => "Auto"
    };

    /// <summary>
    /// Every mode an administrator may choose, for validation and documentation surfaces.
    /// </summary>
    public static IReadOnlyList<SmsOtpDeliveryMode> All { get; } =
    [
        SmsOtpDeliveryMode.Auto,
        SmsOtpDeliveryMode.NativeOtp,
        SmsOtpDeliveryMode.TransactionalTemplate
    ];
}
