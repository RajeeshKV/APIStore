using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Checkout;
using KromicCommerce.Contracts.Orders;
using KromicCommerce.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Checkout and payment verification endpoints.
/// Both require authentication — anonymous checkout is not supported in Phase 5.
/// Server calculates all totals; the frontend only provides address and payment method.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Authorize]
public sealed class CheckoutController(
    IMediator mediator,
    ICurrentUserService currentUser,
    IOptions<RazorpayOptions> razorpayOptions)
    : ControllerBase
{
    /// <summary>
    /// Initiates checkout from the current cart.
    /// Server calculates subtotal, shipping, COD fee, and grand total.
    /// For Razorpay: returns ProviderOrderId and RazorpayKeyId for the frontend widget.
    /// Never accept price/total values from the request body.
    /// </summary>
    [HttpPost("checkout")]
    [ProducesResponseType(typeof(CheckoutResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequest request, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId is null) return Unauthorized();

        var result = await mediator.Send(new CheckoutCommand(
            userId.Value,
            request.ShippingAddress,
            request.PaymentMethod,
            request.CouponCode,
            request.IdempotencyKey), ct);

        if (!result.IsSuccess) return result.Error.ToActionResult();

        // Inject Razorpay KeyId (public, not secret) for frontend widget initialization
        var response = result.Value;
        if (response.ProviderOrderId is not null)
            response = response with { RazorpayKeyId = razorpayOptions.Value.KeyId };

        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// Verifies a Razorpay payment after the frontend checkout widget completes.
    /// The signature is verified server-side — the frontend cannot fake a successful payment.
    /// On success: order is confirmed, inventory is finalized.
    /// On failure: order and payment are marked failed, inventory is released.
    /// </summary>
    [HttpPost("payments/verify")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyPayment(
        [FromBody] RazorpayCallbackRequest request,
        [FromQuery] Guid orderId,
        CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId is null) return Unauthorized();

        var result = await mediator.Send(new VerifyPaymentCommand(
            orderId, userId.Value,
            request.RazorpayPaymentId,
            request.RazorpayOrderId,
            request.RazorpaySignature), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }
}
