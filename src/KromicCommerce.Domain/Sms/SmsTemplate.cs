namespace KromicCommerce.Domain.Sms;

/// <summary>
/// An admin-managed SMS message template for one of the supported gateways.
/// </summary>
/// <remarks>
/// <para>
/// Templates are stored in the database rather than configuration because every supported
/// gateway requires a pre-registered message: Free2SMS matches the body against a DLT-approved
/// template, Twilio Verify requires a verified <c>TemplateSid</c> when one is used, and 2Factor
/// requires an approved dynamic template. Those registrations live in the vendor portal, so the
/// identifiers must be editable per deployment without a redeploy — and the storefront owner has
/// no access to environment variables.
/// </para>
/// <para>
/// A template is scoped to exactly one <see cref="Provider"/>, and at most one template per
/// provider may be active at a time. <see cref="IsActive"/> selects which one is used for OTP
/// delivery; the rest are kept so switching back does not require re-entering them.
/// </para>
/// <para>
/// Templates are OPTIONAL. When a provider has no active template the gateway falls back to its
/// documented default behaviour, so a fresh deployment can send OTPs before anyone has registered
/// a template with a vendor.
/// </para>
/// </remarks>
public sealed class SmsTemplate : AuditableEntity
{
    /// <summary>Placeholder replaced with the generated OTP code.</summary>
    public const string OtpPlaceholder = "{OTP}";

    /// <summary>Placeholder replaced with the OTP lifetime in minutes.</summary>
    public const string ExpiryPlaceholder = "{EXPIRY_MINUTES}";

    /// <summary>Placeholder replaced with the store/brand name, when the template uses it.</summary>
    public const string StoreNamePlaceholder = "{STORE_NAME}";

    private SmsTemplate() { } // EF constructor

    private SmsTemplate(
        SmsProviderKind provider,
        string name,
        string body,
        string? externalTemplateId,
        bool isActive)
    {
        Provider = provider;
        Name = name;
        Body = body;
        ExternalTemplateId = externalTemplateId;
        IsActive = isActive;
    }

    /// <summary>The gateway this template is registered with. Cannot be changed after creation.</summary>
    public SmsProviderKind Provider { get; private set; }

    /// <summary>Admin-facing label, e.g. "Verification code (default)".</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// The message body sent to the gateway, containing the placeholder tokens. Empty for
    /// gateways that do not render a body (Twilio Verify with a <c>TemplateSid</c> and no
    /// custom substitutions), in which case only <see cref="ExternalTemplateId"/> matters.
    /// </summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>
    /// The vendor-side template registration identifier — a Twilio Verify <c>TemplateSid</c>
    /// (<c>HJ…</c>), a Free2SMS DLT template ID (19 digits, stored as text because it exceeds
    /// <see cref="long"/>'s range on some registrations), or a 2Factor template name. Null when
    /// the gateway does not require a pre-registered template.
    /// </summary>
    public string? ExternalTemplateId { get; private set; }

    /// <summary>True when this is the template used for OTP delivery to this provider.</summary>
    public bool IsActive { get; private set; }

    public static SmsTemplate Create(
        SmsProviderKind provider,
        string name,
        string? body,
        string? externalTemplateId,
        bool isActive = true)
    {
        if (!provider.IsSelectable())
            throw new ArgumentException("A template must belong to a selectable provider.", nameof(provider));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Template name is required.", nameof(name));

        // A body is required unless the gateway renders the message from its own registered
        // template; externalTemplateId alone is then sufficient.
        var hasBody = !string.IsNullOrWhiteSpace(body);
        if (!hasBody && string.IsNullOrWhiteSpace(externalTemplateId))
        {
            throw new ArgumentException(
                "A template needs either a message body or a vendor template identifier.", nameof(body));
        }

        return new SmsTemplate(
            provider,
            name.Trim(),
            (body ?? string.Empty).Trim(),
            string.IsNullOrWhiteSpace(externalTemplateId) ? null : externalTemplateId.Trim(),
            isActive);
    }

    public void Update(string name, string? body, string? externalTemplateId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Template name is required.", nameof(name));

        var hasBody = !string.IsNullOrWhiteSpace(body);
        if (!hasBody && string.IsNullOrWhiteSpace(externalTemplateId))
        {
            throw new ArgumentException(
                "A template needs either a message body or a vendor template identifier.", nameof(body));
        }

        Name = name.Trim();
        Body = (body ?? string.Empty).Trim();
        ExternalTemplateId = string.IsNullOrWhiteSpace(externalTemplateId) ? null : externalTemplateId.Trim();
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Substitutes the delivery-time values into <see cref="Body"/>.
    /// Used only by gateways that accept a rendered body; gateways driven purely by a vendor
    /// template identifier ignore <see cref="Body"/>.
    /// </summary>
    public string Render(string otp, int expiryMinutes, string? storeName = null)
        => Body
            .Replace(OtpPlaceholder, otp, StringComparison.Ordinal)
            .Replace(ExpiryPlaceholder, expiryMinutes.ToString(
                System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace(StoreNamePlaceholder, storeName ?? string.Empty, StringComparison.Ordinal);
}
