namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// A product-level attribute definition (e.g. "Color", "Material").
/// Values are stored as ProductAttributeValue records linked to this attribute.
/// Used for storefront filtering and variant selection.
/// </summary>
public sealed class ProductAttribute : Entity
{
    private ProductAttribute() { } // EF constructor

    public static ProductAttribute Create(Guid productId, string name, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Attribute name is required.", nameof(name));

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
}

/// <summary>
/// A single selectable value for a ProductAttribute (e.g. "Red", "Blue").
/// </summary>
public sealed class ProductAttributeValue : Entity
{
    private ProductAttributeValue() { } // EF constructor

    public static ProductAttributeValue Create(Guid attributeId, string value, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Attribute value is required.", nameof(value));

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
}
