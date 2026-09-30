namespace KromicCommerce.Contracts.Cart;

/// <summary>
/// Request to apply a coupon to the current cart.
///
/// The backend re-validates every coupon rule and returns the complete recalculated checkout
/// summary. No discount amount may be sent by the client.
/// </summary>
public sealed record ApplyCouponRequest(
    /// <summary>Coupon code exactly as the customer entered it. Normalised server-side.</summary>
    string CouponCode);
