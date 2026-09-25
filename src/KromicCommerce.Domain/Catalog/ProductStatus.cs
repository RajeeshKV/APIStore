namespace KromicCommerce.Domain.Catalog;

/// <summary>
/// Product lifecycle status. Persisted as strings.
/// Draft → Active (publish)
/// Active → Archived (archive)
/// Archived → Active (re-publish)
/// </summary>
public enum ProductStatus
{
    Draft,
    Active,
    Archived
}
