using Asp.Versioning;
using KromicCommerce.Application.Features.Admin.SmsTemplates;
using KromicCommerce.Contracts.Admin;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Admin-only SMS provider configuration endpoints.
///
/// GET returns provider capability metadata that drives the admin form.
/// No template management endpoints exist — provider-specific message templates are
/// configured as part of the provider's settings, not as separate entities.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/integrations/sms")]
[Authorize(Policy = "AdminOnly")]
public sealed class SmsProvidersController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Lists the gateways an administrator may select, so the admin screen and the backend
    /// always agree on the supported set and the fields each requires.
    /// </summary>
    [HttpGet("providers")]
    [ProducesResponseType(typeof(IReadOnlyList<SmsProviderOptionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProviders(CancellationToken ct)
    {
        var result = await mediator.Send(new GetSmsProvidersQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }
}
