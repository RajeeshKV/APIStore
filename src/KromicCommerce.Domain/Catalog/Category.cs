namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// Product category. Supports one level of nesting via ParentCategoryId.
/// Slug must be unique across categories (enforced by DB unique index).
/// </summary>
public sealed class Category : AuditableEntity
{
    private Category() { } // EF constructor

    public static Category Create(
        string name,
        string slug,
        string? description,
        Guid? parentCategoryId,
        int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Category name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Category slug is required.", nameof(slug));

        var category = new Category
        {
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Description = description?.Trim(),
            ParentCategoryId = parentCategoryId,
            SortOrder = sortOrder,
            IsActive = true
        };
        category.RaiseDomainEvent(new CategoryCreatedEvent(category.Id, category.Slug));
        return category;
    }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid? ParentCategoryId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Cloudinary image for this category (optional).</summary>
    public string? ImagePublicId { get; private set; }
    public string? ImageUrl { get; private set; }

    // Navigation
    public Category? ParentCategory { get; private set; }
    private readonly List<Category> _children = [];
    public IReadOnlyList<Category> Children => _children.AsReadOnly();

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void Update(string name, string slug, string? description, Guid? parentCategoryId, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Slug required.", nameof(slug));

        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = description?.Trim();
        ParentCategoryId = parentCategoryId;
        SortOrder = sortOrder;
    }

    public void SetImage(string publicId, string url)
    {
        ImagePublicId = publicId.Trim();
        ImageUrl = url.Trim();
    }

    public void ClearImage()
    {
        ImagePublicId = null;
        ImageUrl = null;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
