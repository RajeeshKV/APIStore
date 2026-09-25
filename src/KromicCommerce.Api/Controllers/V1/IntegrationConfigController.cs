using Asp.Versioning;
using KromicCommerce.Application.Features.Admin.Integrations;
using KromicCommerce.Contracts.Admin;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Admin-only integration configuration endpoints.
/// GET endpoints return masked/safe status — never raw secrets.
/// PUT endpoints accept new credentials, encrypt them, and write to Outbox for processing.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/integrations")]
[Authorize(Policy = "AdminOnly")]
public sealed class IntegrationConfigController(IMediator mediator) : ControllerBase
{
    [HttpGet("payment")]
    [ProducesResponseType(typeof(IntegrationStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayment(CancellationToken ct)
    {
        var result = await mediator.Send(new GetPaymentIntegrationStatusQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPut("payment")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdatePayment(
        [FromBody] UpdateRazorpayConfigRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateRazorpayConfigCommand(
            request.Enabled, request.KeyId, request.KeySecret, request.WebhookSecret), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    [HttpGet("google")]
    [ProducesResponseType(typeof(IntegrationStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetGoogle(CancellationToken ct)
    {
        var result = await mediator.Send(new GetGoogleIntegrationStatusQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPut("google")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateGoogle(
        [FromBody] UpdateGoogleOAuthConfigRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateGoogleOAuthConfigCommand(
            request.Enabled, request.ClientId, request.ClientSecret, request.RedirectUri), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    [HttpGet("sms")]
    [ProducesResponseType(typeof(IntegrationStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSms(CancellationToken ct)
    {
        var result = await mediator.Send(new GetSmsIntegrationStatusQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPut("sms")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateSms(
        [FromBody] UpdateSmsConfigRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateSmsConfigCommand(
            request.Enabled, request.Provider, request.ProviderSettings), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    [HttpGet("email")]
    [ProducesResponseType(typeof(IntegrationStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmail(CancellationToken ct)
    {
        var result = await mediator.Send(new GetEmailIntegrationStatusQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPut("email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateEmail(
        [FromBody] UpdateEmailConfigRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateEmailConfigCommand(
            request.Enabled, request.Mode, request.SenderName,
            request.SenderEmail, request.ApiKey), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
