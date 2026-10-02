namespace KromicCommerce.Application.Abstractions.Sms;

/// <summary>
/// The setting names each SMS provider accepts, and which of them are required.
/// </summary>
/// <remarks>
/// Used to validate an administrator's configuration write. Without it, a misspelled key was
/// stored, reported as saved, and then never read — the admin screen looked configured while
/// sends failed on missing credentials. Keeping the list here, next to the provider set, means
/// adding a gateway means extending one table rather than hunting down every validation site.
///
/// Names match the corresponding <c>*Options</c> property on the Infrastructure options class
/// for that provider.
/// </remarks>
public static class SmsSettingNames
{
    /// <summary>API key / token. Present for every gateway.</summary>
    public const string ApiKey = "ApiKey";

    public const string SenderId = "SenderId";
    public const string BaseUrl = "BaseUrl";
    public const string Route = "Route";

    public const string AccountSid = "AccountSid";
    public const string AuthToken = "AuthToken";
    public const string ServiceSid = "ServiceSid";
    public const string MessagingServiceSid = "MessagingServiceSid";

    /// <summary>
    /// Preferred OTP route, or the single transactional route for gateways with no native OTP.
    /// Retained because administrators may already have saved it against 2Factor.
    /// </summary>
    public const string SendPath = "SendPath";

    public const string OtpPath = "OtpPath";
    public const string TransactionalPath = "TransactionalPath";
    public const string ApiKeyHeader = "ApiKeyHeader";
    public const string TemplateNameField = "TemplateNameField";
    public const string Channel = "Channel";
    public const string OtpVariableName = "OtpVariableName";
    public const string ExpiryVariableName = "ExpiryVariableName";

    public const string MessagingBaseUrl = "MessagingBaseUrl";
    public const string MessagingPath = "MessagingPath";

    /// <summary>
    /// Which route to use: <c>Auto</c> (native first, fall back to transactional),
    /// <c>NativeOtp</c>, or <c>TransactionalTemplate</c>.
    /// </summary>
    public const string DeliveryMode = "DeliveryMode";

    private static readonly IReadOnlyDictionary<SmsProviderKind, string[]> Optional =
        new Dictionary<SmsProviderKind, string[]>
        {
            [SmsProviderKind.TwoFactor] =
            [
                SenderId, BaseUrl, DeliveryMode,
                SendPath, OtpPath, TransactionalPath,
                ApiKeyHeader, TemplateNameField, Channel,
                OtpVariableName, ExpiryVariableName
            ],
            [SmsProviderKind.Free2Sms] = [SenderId, BaseUrl, Route, DeliveryMode],
            [SmsProviderKind.Twilio] =
                [MessagingServiceSid, BaseUrl, SenderId, DeliveryMode, MessagingBaseUrl, MessagingPath]
        };

    private static readonly IReadOnlyDictionary<SmsProviderKind, string[]> RequiredSettings =
        new Dictionary<SmsProviderKind, string[]>
        {
            [SmsProviderKind.TwoFactor] = [ApiKey],
            [SmsProviderKind.Free2Sms] = [ApiKey, SenderId],
            [SmsProviderKind.Twilio] = [AccountSid, AuthToken, ServiceSid]
        };

    /// <summary>Every setting name accepted for <paramref name="provider"/>, required included.</summary>
    public static IReadOnlyList<string> All(SmsProviderKind provider) =>
        Required(provider).Concat(Optional[provider]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Settings without which the provider cannot send. Required when SMS is enabled.</summary>
    public static IReadOnlyList<string> Required(SmsProviderKind provider)
        => RequiredSettings.TryGetValue(provider, out var required) ? required : [];

    /// <summary>True when <paramref name="name"/> is a real setting for the provider, ignoring case.</summary>
    public static bool IsKnown(SmsProviderKind provider, string? name)
        => name is not null
           && Optional.TryGetValue(provider, out var optional)
           && (Required(provider).Contains(name.Trim(), StringComparer.OrdinalIgnoreCase)
               || optional.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase));
}
