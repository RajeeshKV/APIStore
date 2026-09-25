namespace KromicCommerce.Contracts.Catalog;

/// <summary>
/// Reorder product images by supplying the desired sort order for each image ID.
/// All image IDs belonging to the product must be included.
/// </summary>
public sealed record ReorderImagesRequest(
    IReadOnlyList<ImageSortOrderItem> Items);

public sealed record ImageSortOrderItem(Guid ImageId, int SortOrder);
