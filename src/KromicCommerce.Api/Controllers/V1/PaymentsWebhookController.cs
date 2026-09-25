using Asp.Versioning;
using KromicCommerce.Application.Features.Checkout;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Razorpay webhook receiver.
/// No [Authorize] — Razorpay does not send JWT tokens.
/// Signature verification happens inside the handler — never trust raw webhook data.
/// Endpoint must return 200 quickly; heavy processing is async via Outbox.
/// Duplicate webhooks are safely ignored (idempotent).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payments/webhook")]
public sealed class PaymentsWebhookController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Webhook(CancellationToken ct)
    {
        // Read raw body — must not be bound by model binding
        Request.EnableBuffering();
        using var reader = new System.IO.StreamReader(Request.Body, leaveOpen: true);
        var rawPayload = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var signature = Request.Headers["X-Razorpay-Signature"].FirstOrDefault() ?? string.Empty;

        var result = await mediator.Send(
            new HandlePaymentWebhookCommand(rawPayload, signature, "Razorpay"), ct);

        // Always return 200 to Razorpay — failed signature returns 401
        return result.IsSuccess ? Ok() : result.Error.ToActionResult();
    }
}
