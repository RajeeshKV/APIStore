namespace KromicCommerce.Domain.Store;

/// <summary>
/// Supported store policy types. Persisted as strings.
/// Each type maps to a distinct policy page on the storefront.
/// </summary>
public enum PolicyType
{
    TermsConditions,
    PrivacyPolicy,
    RefundPolicy,
    CancellationPolicy,
    ReturnPolicy,
    ShippingPolicy,
    OrderPolicy
}
