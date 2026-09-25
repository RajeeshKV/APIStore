using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Cart.AddCartItem;
using KromicCommerce.Application.Features.Cart.ClearCart;
using KromicCommerce.Application.Features.Cart.GetCart;
using KromicCommerce.Application.Features.Cart.RemoveCartItem;
using KromicCommerce.Application.Features.Cart.UpdateCartItem;
using KromicCommerce.Contracts.Cart;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Shopping cart endpoints.
/// Supports both authenticated customers (JWT) and anonymous visitors (X-Cart-Token header).
/// The server validates product availability, variant validity, and inventory on every mutation.
/// Never trust frontend-provided prices, discounts, or totals.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/cart")]
public sealed class CartController(IMediator mediator, ICurrentUserService currentUser)
    : ControllerBase
{
    /// <summary>Returns the current cart for the authenticated user or anonymous session.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCart(CancellationToken ct)
    {
        var (customerId, anonId) = ResolveCartOwner();
        var result = await mediator.Send(new GetCartQuery(customerId, anonId), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>Adds a product/variant to the cart. Server validates availability and inventory.</summary>
    [HttpPost("items")]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddItem(
        [FromBody] AddCartItemRequest request, CancellationToken ct)
    {
        var (customerId, anonId) = ResolveCartOwner();
        var result = await mediator.Send(
            new AddCartItemCommand(customerId, anonId, request.ProductId, request.VariantId, request.Quantity), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>Updates the quantity of a specific cart item.</summary>
    [HttpPut("items/{itemId:guid}")]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateItem(
        Guid itemId, [FromBody] UpdateCartItemRequest request, CancellationToken ct)
    {
        var (customerId, anonId) = ResolveCartOwner();
        var result = await mediator.Send(
            new UpdateCartItemCommand(customerId, anonId, itemId, request.Quantity), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>Removes a specific item from the cart (idempotent).</summary>
    [HttpDelete("items/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveItem(Guid itemId, CancellationToken ct)
    {
        var (customerId, anonId) = ResolveCartOwner();
        await mediator.Send(new RemoveCartItemCommand(customerId, anonId, itemId), ct);
        return NoContent();
    }

    /// <summary>Clears all items from the cart (idempotent).</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ClearCart(CancellationToken ct)
    {
        var (customerId, anonId) = ResolveCartOwner();
        await mediator.Send(new ClearCartCommand(customerId, anonId), ct);
        return NoContent();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private (Guid? CustomerId, string? AnonId) ResolveCartOwner()
    {
        if (currentUser.IsAuthenticated && currentUser.UserId.HasValue)
            return (currentUser.UserId.Value, null);

        // Anonymous: read secure server-issued token from header
        var token = Request.Headers["X-Cart-Token"].FirstOrDefault();
        return (null, string.IsNullOrWhiteSpace(token) ? null : token);
    }
}
