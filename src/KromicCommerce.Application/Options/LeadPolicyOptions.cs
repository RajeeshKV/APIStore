namespace KromicCommerce.Application.Options;

/// <summary>
/// Lead-capture policy, bridged from Infrastructure configuration into the Application layer.
///
/// <para>
/// <see cref="NotificationEmail"/> is deliberately <b>not</b> merchant-editable at runtime. The
/// endpoint that uses it is public and unauthenticated, so the destination of its outbound mail
/// must not be settable from anywhere a store owner can reach — otherwise the endpoint becomes a
/// relay pointed at an address of the caller's choosing. The submitted visitor address is used as
/// Reply-To only.
/// </para>
/// </summary>
public sealed class LeadPolicyOptions
{
    /// <summary>Where lead notifications are delivered. Never taken from a request.</summary>
    public string? NotificationEmail { get; set; }

    /// <summary>Display name for the notification recipient.</summary>
    public string? NotificationName { get; set; }
}