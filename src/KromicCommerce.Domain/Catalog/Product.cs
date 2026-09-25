namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// Product aggregate root.
///
/// Price rules:
///   - Price must be >= 0 (zero-price products are valid for free/promotional items).
///   - CompareAtPrice, when set, must be strictly > Price (for crossed-out "was" display).
///   - Never trust client-supplied prices — always recalculate server-side.
///
/// Slug uniqueness is enforced by a DB unique index; the handler checks for
/// duplicates before persisting to return a friendly error first.
/// </summary>
public sealed class Product : AuditableEntity
{
    private Product() { } // EF constructor

    public static Product Create(
        string name,
        string slug,
        string? sku,
        decimal price,
        Guid? categoryId,
        Guid? brandId)
    {
        ValidatePrice(price, null);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Slug required.", nameof(slug));

        var product = new Product
        {
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Sku = sku?.Trim(),
            Price = price,
            CategoryId = categoryId,
            BrandId = brandId,
            Status = ProductStatus.Draft
        };
        product.RaiseDomainEvent(new ProductCreatedEvent(product.Id, product.Slug));
        return product;
    }

    // -----------------------------------------------------------------------
    // Core fields
    // -----------------------------------------------------------------------
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Sku { get; private set; }
    public string? Description { get; private set; }
    public string? ShortDescription { get; private set; }

    // -----------------------------------------------------------------------
    // Pricing (decimal — never double/float)
    // -----------------------------------------------------------------------
    public decimal Price { get; private set; }

    /// <summary>
    /// Original / "was" price shown crossed out.
    /// Must be > Price when set. Null = no compare-at display.
    /// </summary>
    public decimal? CompareAtPrice { get; private set; }

    // -----------------------------------------------------------------------
    // Categorisation
    // -----------------------------------------------------------------------
    public Guid? CategoryId { get; private set; }
    public Guid? BrandId { get; private set; }
    public ProductStatus Status { get; private set; }

    // -----------------------------------------------------------------------
    // SEO
    // -----------------------------------------------------------------------
    public string? MetaTitle { get; private set; }
    public string? MetaDescription { get; private set; }
    public string? MetaKeywords { get; private set; }

    // -----------------------------------------------------------------------
    // Flags
    // -----------------------------------------------------------------------
    public bool IsFeatured { get; private set; }
    public bool IsTaxable { get; private set; } = true;

    // Navigation
    public Category? Category { get; private set; }
    public Brand? Brand { get; private set; }

    private readonly List<ProductImage> _images = [];
    public IReadOnlyList<ProductImage> Images => _images.AsReadOnly();

    private readonly List<ProductVariant> _variants = [];
    public IReadOnlyList<ProductVariant> Variants => _variants.AsReadOnly();

    private readonly List<ProductAttribute> _attributes = [];
    public IReadOnlyList<ProductAttribute> Attributes => _attributes.AsReadOnly();

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void UpdateDetails(
        string name,
        string slug,
        string? sku,
        string? description,
        string? shortDescription,
        Guid? categoryId,
        Guid? brandId,
        bool isFeatured,
        bool isTaxable)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Slug required.", nameof(slug));

        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Sku = sku?.Trim();
        Description = description?.Trim();
        ShortDescription = shortDescription?.Trim();
        CategoryId = categoryId;
        BrandId = brandId;
        IsFeatured = isFeatured;
        IsTaxable = isTaxable;
    }

    public void UpdatePricing(decimal price, decimal? compareAtPrice)
    {
        ValidatePrice(price, compareAtPrice);
        Price = price;
        CompareAtPrice = compareAtPrice;
    }

    public void UpdateSeo(string? metaTitle, string? metaDescription, string? metaKeywords)
    {
        MetaTitle = metaTitle?.Trim();
        MetaDescription = metaDescription?.Trim();
        MetaKeywords = metaKeywords?.Trim();
    }

    public void Publish()
    {
        if (Status == ProductStatus.Active) return;
        Status = ProductStatus.Active;
        RaiseDomainEvent(new ProductStatusChangedEvent(Id, Status));
    }

    public void Archive()
    {
        if (Status == ProductStatus.Archived) return;
        Status = ProductStatus.Archived;
        RaiseDomainEvent(new ProductStatusChangedEvent(Id, Status));
    }

    public void Unpublish()
    {
        if (Status == ProductStatus.Draft) return;
        Status = ProductStatus.Draft;
        RaiseDomainEvent(new ProductStatusChangedEvent(Id, Status));
    }

    // -----------------------------------------------------------------------
    // Effective price — single source of truth
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the effective selling price for a given variant (or the base product price
    /// when no variant is provided).
    ///
    /// Rule:
    ///   variant.PriceOverride != null → EffectivePrice = variant.PriceOverride
    ///   variant.PriceOverride == null → EffectivePrice = Product.Price
    ///   variant == null               → EffectivePrice = Product.Price
    ///
    /// This is the ONLY place this calculation lives. Do not duplicate it in
    /// cart, checkout, orders, payments, promotions, controllers, or background jobs.
    /// </summary>
    public decimal GetEffectivePrice(ProductVariant? variant = null)
        => variant?.PriceOverride ?? Price;

    // -----------------------------------------------------------------------
    // Guard
    // -----------------------------------------------------------------------

    private static void ValidatePrice(decimal price, decimal? compareAtPrice)
    {
        if (price < 0)
            throw new ArgumentException("Price must be >= 0.", nameof(price));
        if (compareAtPrice.HasValue && compareAtPrice.Value <= price)
            throw new ArgumentException(
                "Compare-at price must be greater than the selling price.", nameof(compareAtPrice));
    }
}
