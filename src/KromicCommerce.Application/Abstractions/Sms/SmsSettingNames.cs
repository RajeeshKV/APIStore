namespace KromicCommerce.Application.Abstractions.Sms;

/// <summary>
/// The setting names each SMS provider accepts, and which of them are required.
/// </summary>
/// <remarks>
/// <para>
/// Names match the corresponding <c>*Options</c> property on the Infrastructure options class
/// for that provider.
/// </para>
/// <para>
/// The key list is derived from <see cref="SmsProviderFieldSchema"/> rather than maintained
/// separately, so a field that is rendered by the admin form is by construction a field that is
/// accepted on save. The two tables used to be independent, which is how a setting could exist in
/// the documentation while the validator rejected it.
/// </para>
/// </remarks>
public static class SmsSettingNames
{
    // ---------------------------------------------------------------------
    // Key names. These match the corresponding options-class property names and
    // are used as the wire keys for PUT /admin/integrations/sms.
    // ---------------------------------------------------------------------

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
    /// Legacy single-route name, retained because administrators may already have saved it against
    /// 2Factor before the native/transactional split. Honoured as an alias for
    /// <see cref="OtpPath"/> so upgrading does not silently point them at a different route.
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

    /// <summary>
    /// Extra keys accepted for a provider but not rendered as inputs, because they are legacy
    /// aliases rather than something an administrator should newly configure.
    /// </summary>
    private static readonly IReadOnlyDictionary<SmsProviderKind, string[]> Aliases =
        new Dictionary<SmsProviderKind, string[]>
        {
            [SmsProviderKind.TwoFactor] = [SendPath]
        };

    /// <summary>Settings without which the provider cannot send. Required when SMS is enabled.</summary>
    public static IReadOnlyList<string> Required(SmsProviderKind provider)
        => SmsProviderFieldSchema.For(provider).RequiredSettings;

    /// <summary>Every setting name accepted for <paramref name="provider"/>, required included.</summary>
    public static IReadOnlyList<string> All(SmsProviderKind provider)
    {
        var fields = SmsProviderFieldSchema.For(provider).Settings.Select(f => f.Key).ToList();

        if (Aliases.TryGetValue(provider, out var aliases))
            fields.AddRange(aliases);

        return fields.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>True when <paramref name="name"/> is a real setting for the provider, ignoring case.</summary>
    public static bool IsKnown(SmsProviderKind provider, string? name)
        => !string.IsNullOrWhiteSpace(name) && All(provider).Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);
}
