using Asp.Versioning;
using KromicCommerce.Application.Features.Storefront.Products.GetFeaturedProducts;
using KromicCommerce.Application.Features.Storefront.Products.GetRelatedProducts;
using KromicCommerce.Application.Features.Storefront.Products.GetStorefrontProductBySlug;
using KromicCommerce.Application.Features.Storefront.Products.GetStorefrontProducts;
using KromicCommerce.Contracts.Catalog;
using KromicCommerce.Contracts.Common;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Public storefront product endpoints — no authentication required.
/// All responses are customer-safe: no inventory internals, no admin fields, no Cloudinary credentials.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/store/products")]
public sealed class StorefrontProductsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Browse the public product catalog.
    /// Returns Active products only. Supports search, category/brand slug filters,
    /// attribute filters, price range, stock-only toggle, sort, and pagination.
    ///
    /// Attribute filter format:
    ///   ?attributeFilters[0].attributeName=Color&amp;attributeFilters[0].attributeValue=Black
    ///   Multiple values for the same name are OR-combined.
    ///   Different attribute names are AND-combined.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<StorefrontProductSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetProducts(
        [FromQuery] StorefrontProductQueryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetStorefrontProductsQuery(request), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Get full product detail by slug. Returns only Active products.
    /// Includes images (sorted), attributes, variants with effective prices and stock,
    /// delivery estimate, and currency.
    /// Returns 404 for Draft/Archived products — never exposes administrative status.
    /// </summary>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(StorefrontProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProduct(
        string slug,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetStorefrontProductBySlugQuery(slug), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Get related products for a product detail page.
    /// Returns Active products in the same category, preferring same brand.
    /// Default limit: 8. Maximum: 20.
    /// Returns empty list (not 404) when no related products exist.
    /// </summary>
    [HttpGet("{slug}/related")]
    [ProducesResponseType(typeof(IReadOnlyList<StorefrontProductSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRelated(
        string slug,
        [FromQuery] int limit = 8,
        CancellationToken cancellationToken = default)
    {
        // Resolve the product to get Id/CategoryId/BrandId
        var productResult = await mediator.Send(
            new GetStorefrontProductBySlugQuery(slug), cancellationToken);

        if (productResult.IsFailure)
            return productResult.Error.ToActionResult();

        var p = productResult.Value;

        var result = await mediator.Send(
            new GetRelatedProductsQuery(p.Id, p.CategoryId, p.BrandId, limit),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Get featured products for homepage/banner display.
    /// Returns Active + IsFeatured products.
    /// Default limit: 12. Maximum: 50.
    /// </summary>
    [HttpGet("/api/v{version:apiVersion}/store/featured")]
    [ProducesResponseType(typeof(IReadOnlyList<StorefrontProductSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFeatured(
        [FromQuery] int limit = 12,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetFeaturedProductsQuery(limit), cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }
}
