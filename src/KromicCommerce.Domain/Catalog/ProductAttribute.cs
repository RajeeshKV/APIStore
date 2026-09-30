namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// A product-level attribute definition (e.g. "Color", "Storage", "Size").
/// Values are stored as ProductAttributeValue records linked to this attribute.
/// Used for storefront filtering and variant selection.
///
/// Attributes are generic: the catalogue has no hard-coded knowledge of size, colour or
/// storage. A product declares the axes it varies on and a variant references one value per
/// axis, which is what identifies that variant.
/// </summary>
public sealed class ProductAttribute : Entity
{
    private ProductAttribute() { } // EF constructor

    public static ProductAttribute Create(Guid productId, string name, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Attribute name is required.", nameof(name));
        if (sortOrder < 0)
            throw new ArgumentException("Attribute sort order must be >= 0.", nameof(sortOrder));

        return new ProductAttribute
        {
            ProductId = productId,
            Name = name.Trim(),
            SortOrder = sortOrder
        };
    }

    public Guid ProductId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }

    // Navigation
    public Product Product { get; private set; } = null!;
    private readonly List<ProductAttributeValue> _values = [];
    public IReadOnlyList<ProductAttributeValue> Values => _values.AsReadOnly();

    /// <summary>Adds a value to the in-memory collection so it can be read back before saving.</summary>
    public void AddValue(ProductAttributeValue value) => _values.Add(value);
}

/// <summary>
/// A single selectable value for a ProductAttribute (e.g. "128GB", "Red", "XL").
/// </summary>
public sealed class ProductAttributeValue : Entity
{
    private ProductAttributeValue() { } // EF constructor

    public static ProductAttributeValue Create(Guid attributeId, string value, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Attribute value is required.", nameof(value));
        if (sortOrder < 0)
            throw new ArgumentException("Attribute value sort order must be >= 0.", nameof(sortOrder));

        return new ProductAttributeValue
        {
            AttributeId = attributeId,
            Value = value.Trim(),
            SortOrder = sortOrder
        };
    }

    public Guid AttributeId { get; private set; }
    public string Value { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }

    // Navigation
    public ProductAttribute Attribute { get; private set; } = null!;

    /// <summary>Renames the value. Used when an admin edits an existing selectable value in place.</summary>
    public void Rename(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Attribute value is required.", nameof(value));

        Value = value.Trim();
    }

    public void SetSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
            throw new ArgumentException("Attribute value sort order must be >= 0.", nameof(sortOrder));

        SortOrder = sortOrder;
    }
}
