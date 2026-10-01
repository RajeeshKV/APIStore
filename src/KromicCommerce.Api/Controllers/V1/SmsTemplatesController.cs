using Asp.Versioning;
using KromicCommerce.Application.Features.Admin.SmsTemplates;
using KromicCommerce.Contracts.Admin;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Admin-only management of SMS message templates.
///
/// Templates are per-provider and optional: a provider with no active template still sends,
/// using its documented default. At most one template per provider is active; activating a
/// second demotes the first, so a send never depends on which row the query happened to find.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/integrations/sms")]
[Authorize(Policy = "AdminOnly")]
public sealed class SmsTemplatesController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Lists the gateways an administrator may select, so the admin screen and the backend
    /// always agree on the supported set.
    /// </summary>
    [HttpGet("providers")]
    [ProducesResponseType(typeof(IReadOnlyList<SmsProviderOptionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProviders(CancellationToken ct)
    {
        var result = await mediator.Send(new GetSmsProvidersQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>Lists every configured template, active or not.</summary>
    [HttpGet("templates")]
    [ProducesResponseType(typeof(IReadOnlyList<SmsTemplateResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTemplates(CancellationToken ct)
    {
        var result = await mediator.Send(new GetSmsTemplatesQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPost("templates")]
    [ProducesResponseType(typeof(SmsTemplateResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSmsTemplateRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateSmsTemplateCommand(
            request.Provider, request.Name, request.Body, request.ExternalTemplateId, request.IsActive), ct);

        return result.IsSuccess
            ? CreatedAtAction(nameof(GetTemplates), result.Value)
            : result.Error.ToActionResult();
    }

    /// <summary>Edits a template. The provider cannot be changed after creation.</summary>
    [HttpPut("templates/{id:guid}")]
    [ProducesResponseType(typeof(SmsTemplateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateSmsTemplateRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateSmsTemplateCommand(
            id, request.Name, request.Body, request.ExternalTemplateId, request.IsActive), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>
    /// Deletes a template. If it was the active one for its provider, delivery falls back to the
    /// provider's default message.
    /// </summary>
    [HttpDelete("templates/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteSmsTemplateCommand(id), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
