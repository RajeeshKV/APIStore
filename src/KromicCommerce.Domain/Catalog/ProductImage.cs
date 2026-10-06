namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// A Cloudinary image associated with a product.
/// Sort order controls display sequence; IsPrimary marks the hero image.
/// </summary>
public sealed class ProductImage : Entity
{
    private ProductImage() { } // EF constructor

    public static ProductImage Create(
        Guid productId,
        MediaAsset asset,
        int sortOrder,
        bool isPrimary,
        Guid? variantId = null)
        => new()
        {
            ProductId = productId,
            VariantId = variantId,
            Asset = asset,
            SortOrder = sortOrder,
            IsPrimary = isPrimary,
            CreatedAt = DateTime.UtcNow
        };

    public Guid ProductId { get; private set; }

    /// <summary>
    /// Optional variant this image belongs to. Null means the image is a product-level asset
    /// (the general gallery). When set, the image is part of that variant's gallery.
    /// </summary>
    public Guid? VariantId { get; private set; }

    public MediaAsset Asset { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public bool IsPrimary { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Navigation
    public Product Product { get; private set; } = null!;
    /// <summary>Null for product-level images.</summary>
    public ProductVariant? Variant { get; private set; } = null!;

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void UpdateSortOrder(int sortOrder) => SortOrder = sortOrder;
    public void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;
}
