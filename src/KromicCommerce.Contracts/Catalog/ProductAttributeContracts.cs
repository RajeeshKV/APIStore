namespace KromicCommerce.Contracts.Catalog;

// -------------------------------------------------------------------------
// Product variant attributes
//
// Attributes are generic: an admin defines the axes a product varies on
// (Storage, Colour, Size, Capacity, ...) and the selectable values for each.
// A variant then references one value per axis, which is what identifies it.
//
// The existing AttributeValueIds CSV field remains the storage format; these
// DTOs are the resolved, display-ready view the frontend needs to build a
// variant selector without a second round trip.
// -------------------------------------------------------------------------

/// <summary>A resolved attribute value on a variant, ready for display.</summary>
public sealed record VariantAttributeValueResponse(
    /// <summary>AttributeValue ID referenced by the variant.</summary>
    Guid AttributeValueId,

    /// <summary>Attribute ID this value belongs to.</summary>
    Guid AttributeId,

    /// <summary>Attribute name, e.g. "Storage".</summary>
    string AttributeName,

    /// <summary>Display value, e.g. "128GB".</summary>
    string Value);

/// <summary>Request to create or replace a product's attribute definition and its values.</summary>
public sealed record UpsertProductAttributeRequest(
    /// <summary>Attribute name, e.g. "Storage". Unique per product. Reused when it already exists.</summary>
    string Name,

    /// <summary>
    /// Selectable values for this attribute, in display order. The stored list is replaced
    /// wholesale: values omitted from this request are deleted, and a variant still
    /// referencing a deleted value becomes unconfigured.
    /// </summary>
    IReadOnlyList<ProductAttributeValueRequest> Values);

/// <summary>One selectable value for an attribute.</summary>
public sealed record ProductAttributeValueRequest(
    string Value,

    /// <summary>
    /// Existing value ID to reuse. Null creates a new value. A value that is not named in the
    /// request is deleted, so this is how reordering works as well as editing.
    /// </summary>
    Guid? Id = null);

/// <summary>Response for the attribute management endpoints.</summary>
public sealed record ProductAttributesResponse(
    Guid ProductId,
    IReadOnlyList<ProductAttributeDto> Attributes);
