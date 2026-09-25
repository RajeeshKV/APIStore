using Asp.Versioning;
using KromicCommerce.Application.Features.Orders.Admin;
using KromicCommerce.Contracts.Common;
using KromicCommerce.Contracts.Orders;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Admin order management endpoints.
/// All require AdminOnly — server enforces this, never trusting client role claims.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/orders")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminOrdersController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<OrderSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrders(
        [FromQuery] AdminOrderQueryRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new GetAdminOrdersQuery(request), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetAdminOrderByIdQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPut("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(
        Guid id, [FromBody] UpdateOrderStatusRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateOrderStatusCommand(
            id, request.Status, request.TrackingNumber, request.TrackingProvider, request.Reason), ct);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
