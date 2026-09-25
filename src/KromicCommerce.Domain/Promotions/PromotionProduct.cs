namespace KromicCommerce.Domain.Promotions;

/// <summary>
/// Join entity: links a promotion to a specific product for applicability filtering.
/// Only used when Promotion.Applicability == SpecificProducts.
/// </summary>
public sealed class PromotionProduct
{
    public Guid PromotionId { get; set; }
    public Guid ProductId { get; set; }

    // Navigation
    public Promotion Promotion { get; set; } = null!;
}
