using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Me.Wishlist;
using KromicCommerce.Contracts.Common;
using KromicCommerce.Contracts.Me;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Customer wishlist.
///
/// Ownership is taken from <see cref="ICurrentUserService"/> on every route and is never read
/// from a body, route, or query value. Delete-by-id routes are keyed on product/variant rather
/// than on the wishlist row id so a customer cannot enumerate or act on another account's rows.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/wishlist")]
[Authorize(Policy = "CustomerOrAdmin")]
public sealed class WishlistController(
    IMediator mediator, ICurrentUserService currentUser) : ControllerBase
{
    private Guid? CustomerId => currentUser.UserId;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<WishlistItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (CustomerId is null) return Unauthorized();
        var result = await mediator.Send(new GetWishlistQuery(CustomerId.Value, page, pageSize), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Bulk membership check for product cards: returns the subset of
    /// <c>productIds</c> that are on the caller's wishlist.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(WishlistStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(
        [FromQuery] Guid[] productIds, CancellationToken ct = default)
    {
        if (CustomerId is null) return Unauthorized();

        var result = await mediator.Send(
            new GetWishlistStatusQuery(CustomerId.Value, productIds ?? []), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Idempotent: returns 201 when the entry was created and 200 when it was already saved.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(AddWishlistItemResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(AddWishlistItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Add(
        [FromBody] AddWishlistItemRequest request, CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        if (request is null) return BadRequest();

        var result = await mediator.Send(
            new AddWishlistItemCommand(CustomerId.Value, request.ProductId, request.ProductVariantId), ct);

        if (!result.IsSuccess) return result.Error.ToActionResult();

        return result.Value.Created
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Ok(result.Value);
    }

    [HttpDelete("{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(
        Guid productId, [FromQuery] Guid? productVariantId, CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        var result = await mediator.Send(
            new RemoveWishlistItemCommand(CustomerId.Value, productId, productVariantId), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        if (CustomerId is null) return Unauthorized();
        var result = await mediator.Send(new ClearWishlistCommand(CustomerId.Value), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}