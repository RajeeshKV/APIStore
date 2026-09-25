using Asp.Versioning;
using KromicCommerce.Application.Features.Admin.Promotions;
using KromicCommerce.Contracts.Common;
using KromicCommerce.Contracts.Promotions;
using KromicCommerce.Domain.Promotions;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Admin-only promotion and coupon management.
/// All endpoints require the AdminOnly policy.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/promotions")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminPromotionsController(IMediator mediator) : ControllerBase
{
    /// <summary>List all promotions with optional filtering and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<PromotionSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? couponCode = null,
        [FromQuery] DateTime? startFrom = null,
        [FromQuery] DateTime? startTo = null,
        [FromQuery] string sortBy = "CreatedAtUtc",
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetPromotionsQuery(page, pageSize, isActive, couponCode, startFrom, startTo, sortBy, descending),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>Get a single promotion by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PromotionDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetPromotionByIdQuery(id), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>Create a new promotion.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(PromotionDetailResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePromotionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DiscountType>(request.DiscountType, out var discountType))
            return BadRequest(new { error = $"Invalid DiscountType: {request.DiscountType}" });
        if (!Enum.TryParse<PromotionApplicabilityType>(request.Applicability, out var applicability))
            return BadRequest(new { error = $"Invalid Applicability: {request.Applicability}" });

        var result = await mediator.Send(new CreatePromotionCommand(
            request.Name, request.Description, request.CouponCode,
            discountType, request.DiscountValue, request.MaxDiscountAmount,
            request.MinimumOrderAmount, request.UsageLimit, request.PerCustomerUsageLimit,
            request.StartsAt, request.ExpiresAt, applicability, request.IsFirstOrderOnly,
            request.TargetProductIds, request.TargetCategoryIds),
            cancellationToken);

        if (!result.IsSuccess) return result.Error.ToActionResult();
        return CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>Update an existing promotion.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PromotionDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdatePromotionRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DiscountType>(request.DiscountType, out var discountType))
            return BadRequest(new { error = $"Invalid DiscountType: {request.DiscountType}" });
        if (!Enum.TryParse<PromotionApplicabilityType>(request.Applicability, out var applicability))
            return BadRequest(new { error = $"Invalid Applicability: {request.Applicability}" });

        var result = await mediator.Send(new UpdatePromotionCommand(
            id, request.Name, request.Description,
            discountType, request.DiscountValue, request.MaxDiscountAmount,
            request.MinimumOrderAmount, request.UsageLimit, request.PerCustomerUsageLimit,
            request.StartsAt, request.ExpiresAt, applicability, request.IsFirstOrderOnly,
            request.TargetProductIds, request.TargetCategoryIds),
            cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    /// <summary>Activate a promotion, making it eligible for use.</summary>
    [HttpPost("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ActivatePromotionCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    /// <summary>Deactivate a promotion so it cannot be applied.</summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeactivatePromotionCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    /// <summary>Delete a promotion. Only allowed when inactive and unused.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new DeletePromotionCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
