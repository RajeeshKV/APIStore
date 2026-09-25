namespace KromicCommerce.Domain.Promotions;

/// <summary>How the discount amount is interpreted.</summary>
public enum DiscountType
{
    /// <summary>Discount is a percentage of the eligible subtotal (0–100).</summary>
    Percentage,

    /// <summary>Discount is a fixed monetary amount in the store currency.</summary>
    FixedAmount
}
