using Asp.Versioning;
using KromicCommerce.Application.Features.Catalog.Products.ChangeProductStatus;
using KromicCommerce.Application.Features.Catalog.Products.CreateProduct;
using KromicCommerce.Application.Features.Catalog.Products.GetProducts;
using KromicCommerce.Application.Features.Catalog.Products.UpdateProduct;
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateProductRequest req, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateProductCommand(
            id, req.Name, req.Slug, req.Sku, req.Price, req.CompareAtPrice,
            req.Description, req.ShortDescription, req.CategoryId, req.BrandId,
            req.IsFeatured, req.IsTaxable,
            req.MetaTitle, req.MetaDescription, req.MetaKeywords), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
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
}
