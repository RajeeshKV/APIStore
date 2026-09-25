namespace KromicCommerce.Domain.Promotions;

/// <summary>Defines which part of the cart a promotion applies to.</summary>
public enum PromotionApplicabilityType
{
    /// <summary>Applies to all items in the cart.</summary>
    EntireOrder,

    /// <summary>Applies only to items belonging to specified products.</summary>
    SpecificProducts,

    /// <summary>Applies only to items belonging to specified categories.</summary>
    SpecificCategories
}
