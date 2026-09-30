using Asp.Versioning;
using KromicCommerce.Application.Features.Catalog.Products.ChangeProductStatus;
using KromicCommerce.Application.Features.Catalog.Products.CreateProduct;
using KromicCommerce.Application.Features.Catalog.Products.GetProducts;
using KromicCommerce.Application.Features.Catalog.Products.UpdateProduct;
using KromicCommerce.Application.Features.Catalog.Products.Attributes;
using KromicCommerce.Application.Features.Catalog.Products.Variants;
using KromicCommerce.Contracts.Catalog;
using KromicCommerce.Contracts.Common;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products")]
public sealed class ProductsController(IMediator mediator) : ControllerBase
{
    // -----------------------------------------------------------------------
    // Public
    // -----------------------------------------------------------------------

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ProductSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProducts([FromQuery] ProductQueryRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new GetProductsQuery(req), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var result = await mediator.Send(new GetProductBySlugQuery(slug), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Admin
    // -----------------------------------------------------------------------

    [HttpGet("admin")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(PagedResponse<ProductSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAdminProducts([FromQuery] ProductQueryRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new GetAdminProductsQuery(req), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpGet("admin/{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetProductByIdQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateProductRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateProductCommand(
            req.Name, req.Slug, req.Sku, req.Price, req.CompareAtPrice,
            req.Description, req.ShortDescription, req.CategoryId, req.BrandId,
            req.IsFeatured, req.IsTaxable,
            req.MetaTitle, req.MetaDescription, req.MetaKeywords), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetBySlug), new { slug = result.Value.Slug, version = "1" }, result.Value)
            : result.Error.ToActionResult();
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateProductCommand(
            id, req.Name, req.Slug, req.Sku, req.Price, req.CompareAtPrice,
            req.Description, req.ShortDescription, req.CategoryId, req.BrandId,
            req.IsFeatured, req.IsTaxable,
            req.MetaTitle, req.MetaDescription, req.MetaKeywords), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new PublishProductCommand(id), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new ArchiveProductCommand(id), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new UnpublishProductCommand(id), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Variant management — Admin only
    // -----------------------------------------------------------------------

    /// <summary>List all variants for a product with current stock levels.</summary>
    [HttpGet("{productId:guid}/variants")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(IReadOnlyList<VariantResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVariants(Guid productId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetVariantsQuery(productId), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>Get a single variant with its current stock level.</summary>
    [HttpGet("{productId:guid}/variants/{variantId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(VariantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVariant(Guid productId, Guid variantId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetVariantByIdQuery(productId, variantId), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Create a new variant for a product.
    /// Automatically creates an InventoryItem at 0 stock.
    /// Attribute values must belong to attributes defined on this product.
    /// The attribute combination must be unique among existing variants.
    ///
    /// Omit sortOrder in the body to have the backend append the variant after the current
    /// maximum — the recommended path.
    /// </summary>
    [HttpPost("{productId:guid}/variants")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(VariantResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateVariant(
        Guid productId,
        [FromBody] CreateVariantRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new CreateVariantCommand(productId, req.Sku, req.PriceOverride,
                req.SortOrder, req.AttributeValueIds), ct);

        if (!result.IsSuccess) return result.Error.ToActionResult();
        return CreatedAtAction(nameof(GetVariant),
            new { productId, variantId = result.Value.Id, version = "1" },
            result.Value);
    }

    /// <summary>
    /// Update a variant.
    /// Attribute combination must remain unique. Activate/deactivate via IsActive.
    /// </summary>
    [HttpPut("{productId:guid}/variants/{variantId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(VariantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateVariant(
        Guid productId,
        Guid variantId,
        [FromBody] UpdateVariantRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpdateVariantCommand(productId, variantId, req.Sku, req.PriceOverride,
                req.SortOrder, req.IsActive, req.AttributeValueIds), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Delete a variant and its associated inventory record.
    /// Note: existing order snapshots that reference this variant are unaffected
    /// because order items store a snapshot of the product name, SKU and price
    /// at checkout time.
    /// </summary>
    [HttpDelete("{productId:guid}/variants/{variantId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteVariant(
        Guid productId,
        Guid variantId,
        CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteVariantCommand(productId, variantId), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Variant attributes — the axes a product varies on
    // -----------------------------------------------------------------------

    /// <summary>
    /// List the attribute definitions and selectable values for a product.
    ///
    /// This is what a variant selector is built from: the axes (Storage, Colour, ...) and the
    /// values available on each. A product with no attributes simply returns an empty list and
    /// behaves as a single-variant product.
    /// </summary>
    [HttpGet("{productId:guid}/attributes")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ProductAttributesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAttributes(Guid productId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetProductAttributesQuery(productId), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Create or replace one attribute definition and its values.
    ///
    /// The value list is replaced wholesale: a value not named in the request is deleted, and
    /// any variant still referencing it loses that axis. Pass the existing value's Id to edit
    /// or reorder it rather than recreate it.
    /// </summary>
    [HttpPut("{productId:guid}/attributes")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ProductAttributesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpsertAttribute(
        Guid productId,
        [FromBody] UpsertProductAttributeRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpsertProductAttributeCommand(productId, req.Name, req.Values), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>Delete an attribute definition and all of its values. Idempotent.</summary>
    [HttpDelete("{productId:guid}/attributes/{attributeId:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAttribute(
        Guid productId, Guid attributeId, CancellationToken ct)
    {
        var result = await mediator.Send(
            new DeleteProductAttributeCommand(productId, attributeId), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
