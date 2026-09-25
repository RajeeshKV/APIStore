using Asp.Versioning;
using KromicCommerce.Application.Features.Catalog.Inventory;
using KromicCommerce.Contracts.Catalog;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/inventory")]
[Authorize(Policy = "AdminOnly")]
public sealed class InventoryController(IMediator mediator) : ControllerBase
{
    /// <summary>Set absolute on-hand stock for a product or variant.</summary>
    [HttpPut("{productId:guid}")]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetStock(
        Guid productId,
        [FromQuery] Guid? variantId,
        [FromBody] SetStockRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new SetStockCommand(productId, variantId, req.OnHand, req.LowStockThreshold), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>Apply a stock delta (positive = restock, negative = manual reduction).</summary>
    [HttpPost("{productId:guid}/adjust")]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AdjustStock(
        Guid productId,
        [FromQuery] Guid? variantId,
        [FromBody] AdjustStockRequest req,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new AdjustStockCommand(productId, variantId, req.Delta, req.Reason), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }
}
