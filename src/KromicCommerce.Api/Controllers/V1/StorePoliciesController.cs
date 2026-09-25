using Asp.Versioning;
using KromicCommerce.Application.Features.Admin.Policies;
using KromicCommerce.Contracts.Store;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
public sealed class StorePoliciesController(IMediator mediator) : ControllerBase
{
    /// <summary>Public endpoint — returns only published policies.</summary>
    [HttpGet("api/v{version:apiVersion}/store/policies")]
    [ProducesResponseType(typeof(IReadOnlyList<StorePolicyResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublic(CancellationToken ct)
    {
        var result = await mediator.Send(new GetPublicPoliciesQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpGet("api/v{version:apiVersion}/admin/policies")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(IReadOnlyList<StorePolicyResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await mediator.Send(new GetAdminPoliciesQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPut("api/v{version:apiVersion}/admin/policies")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(StorePolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upsert(
        [FromBody] UpsertStorePolicyRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpsertStorePolicyCommand(
                request.PolicyType, request.Title, request.Content, request.IsPublished), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpDelete("api/v{version:apiVersion}/admin/policies/{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteStorePolicyCommand(id), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
