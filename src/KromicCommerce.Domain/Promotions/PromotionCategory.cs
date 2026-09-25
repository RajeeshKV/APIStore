namespace KromicCommerce.Domain.Promotions;

/// <summary>
/// Join entity: links a promotion to a specific category for applicability filtering.
/// Only used when Promotion.Applicability == SpecificCategories.
/// </summary>
public sealed class PromotionCategory
{
    public Guid PromotionId { get; set; }
    public Guid CategoryId { get; set; }

    // Navigation
    public Promotion Promotion { get; set; } = null!;
}
