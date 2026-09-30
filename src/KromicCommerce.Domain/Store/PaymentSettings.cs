namespace KromicCommerce.Domain.Store;

/// <summary>
/// Razorpay payment gateway credentials stored in the database.
/// Credentials are encrypted by the Application layer before reaching here.
/// KeySecret and WebhookSecret are never stored or returned in plaintext.
/// </summary>
public sealed class PaymentSettings : ValueObject
{
    private PaymentSettings() { }

    public static PaymentSettings Default() => new()
    {
        Enabled = false,
        RazorpayKeyId = null,
        EncryptedRazorpayKeySecret = null,
        EncryptedRazorpayWebhookSecret = null
    };

    public bool Enabled { get; private set; }

    /// <summary>Razorpay Key ID (rzp_live_... / rzp_test_...) — safe to display masked.</summary>
    public string? RazorpayKeyId { get; private set; }

    /// <summary>Encrypted Razorpay Key Secret. Never return or log this.</summary>
    public string? EncryptedRazorpayKeySecret { get; private set; }

    /// <summary>Encrypted Razorpay Webhook Secret. Never return or log this.</summary>
    public string? EncryptedRazorpayWebhookSecret { get; private set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(RazorpayKeyId) &&
        !string.IsNullOrWhiteSpace(EncryptedRazorpayKeySecret) &&
        !string.IsNullOrWhiteSpace(EncryptedRazorpayWebhookSecret);

    internal PaymentSettings WithCredentials(
        string keyId,
        string encryptedKeySecret,
        string encryptedWebhookSecret,
        bool enabled)
        => new()
        {
            Enabled = enabled,
            RazorpayKeyId = keyId.Trim(),
            EncryptedRazorpayKeySecret = encryptedKeySecret,
            EncryptedRazorpayWebhookSecret = encryptedWebhookSecret
        };

    internal PaymentSettings WithEnabled(bool enabled)
        => new()
        {
            Enabled = enabled,
            RazorpayKeyId = RazorpayKeyId,
            EncryptedRazorpayKeySecret = EncryptedRazorpayKeySecret,
            EncryptedRazorpayWebhookSecret = EncryptedRazorpayWebhookSecret
        };

    /// <summary>
    /// Copies every value from <paramref name="other"/> into this instance, in place.
    ///
    /// Required for EF Core owned entities — see the equivalent method on DeliverySettings for
    /// why replacing the reference silently loses the write.
    /// </summary>
    internal void ApplyFrom(PaymentSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Enabled = other.Enabled;
        RazorpayKeyId = other.RazorpayKeyId;
        EncryptedRazorpayKeySecret = other.EncryptedRazorpayKeySecret;
        EncryptedRazorpayWebhookSecret = other.EncryptedRazorpayWebhookSecret;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Enabled;
        yield return RazorpayKeyId;
        yield return EncryptedRazorpayKeySecret;
        yield return EncryptedRazorpayWebhookSecret;
    }
}
