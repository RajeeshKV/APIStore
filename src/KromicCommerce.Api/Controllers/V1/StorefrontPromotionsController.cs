using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Store.Promotions;
using KromicCommerce.Contracts.Promotions;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Customer-facing promotion endpoints.
/// Validate a coupon against the customer's current cart — server-side only.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/store/promotions")]
[Authorize(Policy = "CustomerOrAdmin")]
public sealed class StorefrontPromotionsController(
    IMediator mediator,
    ICurrentUserService currentUser)
    : ControllerBase
{
    /// <summary>
    /// Validate a coupon code against the current cart.
    /// The server recalculates cart contents from the database — never trusts client subtotals.
    /// A valid response at this point does NOT guarantee the coupon will still be valid at checkout.
    /// The checkout handler always re-validates the coupon.
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(CouponValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Validate(
        [FromBody] ValidateCouponRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Unauthorized();

        var result = await mediator.Send(
            new ValidateCouponQuery(request.CouponCode, currentUser.UserId.Value),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }
}
