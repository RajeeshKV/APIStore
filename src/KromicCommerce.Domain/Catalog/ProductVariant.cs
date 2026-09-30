namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// A product variant (e.g. Size: L, Color: Red).
/// Price override is optional — null means the variant inherits the product price.
/// A zero price override is valid (free variant).
/// SKU must be unique when provided (enforced by DB unique index).
/// </summary>
public sealed class ProductVariant : AuditableEntity
{
    private ProductVariant() { } // EF constructor

    public static ProductVariant Create(
        Guid productId,
        string? sku,
        decimal? priceOverride,
        int sortOrder = 0)
    {
        if (priceOverride.HasValue && priceOverride.Value < 0)
            throw new ArgumentException("Variant price override must be >= 0.", nameof(priceOverride));

        return new ProductVariant
        {
            ProductId = productId,
            Sku = sku?.Trim(),
            PriceOverride = priceOverride,
            SortOrder = sortOrder,
            IsActive = true
        };
    }

    public Guid ProductId { get; private set; }

    /// <summary>Optional variant-level SKU. Unique when non-null.</summary>
    public string? Sku { get; private set; }

    /// <summary>When null the product's base price applies.</summary>
    public decimal? PriceOverride { get; private set; }

    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Comma-separated attribute value IDs for display (e.g. "attr_val_id1,attr_val_id2").</summary>
    public string? AttributeValueIds { get; private set; }

    /// <summary>
    /// The attribute value IDs as a typed list, ignoring blanks and unparsable entries.
    /// This is the structured counterpart of <see cref="AttributeValueIds"/> — the CSV column
    /// stays the storage format so existing rows remain readable, while callers work with
    /// real GUIDs instead of parsing strings.
    /// </summary>
    public IReadOnlyList<Guid> ParsedAttributeValueIds =>
        AttributeValueIdsParsed();

    private Guid[] AttributeValueIdsParsed()
    {
        if (string.IsNullOrWhiteSpace(AttributeValueIds)) return [];

        var ids = new List<Guid>(AttributeValueIds!.Count(c => c == ',') + 1);
        foreach (var part in AttributeValueIds.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Guid.TryParse(part.Trim(), out var id))
                ids.Add(id);
        }
        return [.. ids];
    }

    /// <summary>True when the variant has no attribute values and therefore has no options to select.</summary>
    public bool HasAttributeSelection => !string.IsNullOrWhiteSpace(AttributeValueIds);

    // Navigation
    public Product Product { get; private set; } = null!;

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void Update(string? sku, decimal? priceOverride, int sortOrder)
    {
        if (priceOverride.HasValue && priceOverride.Value < 0)
            throw new ArgumentException("Variant price override must be >= 0.", nameof(priceOverride));

        Sku = sku?.Trim();
        PriceOverride = priceOverride;
        SortOrder = sortOrder;
    }

    public void SetAttributeValues(IEnumerable<Guid> attributeValueIds)
        => AttributeValueIds = string.Join(",", attributeValueIds);

    /// <summary>Clears the variant's attribute selection, making it an unconfigured variant.</summary>
    public void ClearAttributeValues() => AttributeValueIds = null;

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
