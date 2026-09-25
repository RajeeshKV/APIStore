using Asp.Versioning;
using KromicCommerce.Application.Features.Admin.Tax;
using KromicCommerce.Contracts.Store;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Admin-only tax configuration.
/// Tax settings are stored in BusinessSettings and cached.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/tax")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminTaxController(IMediator mediator) : ControllerBase
{
    /// <summary>Get the current store tax configuration.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(TaxConfigResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetTaxConfigQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Update the store tax configuration.
    /// Automatically invalidates the business-settings cache.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(TaxConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateTaxConfigRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new UpdateTaxConfigCommand(
                request.TaxEnabled, request.TaxPercentage,
                request.IsPriceInclusive, request.TaxLabel),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }
}
