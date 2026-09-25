using Asp.Versioning;
using KromicCommerce.Application.Features.Store.GetPublicSettings;
using KromicCommerce.Application.Features.Storefront.Brands.GetStorefrontBrandBySlug;
using KromicCommerce.Application.Features.Storefront.Brands.GetStorefrontBrands;
using KromicCommerce.Application.Features.Storefront.Categories.GetStorefrontCategories;
using KromicCommerce.Application.Features.Storefront.Categories.GetStorefrontCategoryBySlug;
using KromicCommerce.Contracts.Catalog;
using KromicCommerce.Contracts.Store;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Public store endpoints — no authentication required.
/// Returns only public-safe fields.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/store")]
public sealed class StoreController(IMediator mediator) : ControllerBase
{
    // -----------------------------------------------------------------------
    // Store settings
    // -----------------------------------------------------------------------

    /// <summary>Returns public store configuration (name, locale, hours, delivery, SEO).</summary>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(PublicBusinessSettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSettings(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPublicSettingsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Categories
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns all active categories with active product counts.
    /// Suitable for navigation menus and category filter sidebars.
    /// </summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<StorefrontCategoryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetStorefrontCategoriesQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Returns a single active category by slug with product count.
    /// Returns 404 for inactive or non-existent categories.
    /// </summary>
    [HttpGet("categories/{slug}")]
    [ProducesResponseType(typeof(StorefrontCategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCategory(string slug, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetStorefrontCategoryBySlugQuery(slug), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    // -----------------------------------------------------------------------
    // Brands
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns all active brands with active product counts.
    /// </summary>
    [HttpGet("brands")]
    [ProducesResponseType(typeof(IReadOnlyList<StorefrontBrandResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBrands(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetStorefrontBrandsQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Returns a single active brand by slug with product count.
    /// Returns 404 for inactive or non-existent brands.
    /// </summary>
    [HttpGet("brands/{slug}")]
    [ProducesResponseType(typeof(StorefrontBrandResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBrand(string slug, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetStorefrontBrandBySlugQuery(slug), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }
}
