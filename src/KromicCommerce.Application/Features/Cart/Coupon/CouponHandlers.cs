using KromicCommerce.Application.Features.Checkout.GetCheckoutSummary;

namespace KromicCommerce.Application.Features.Cart.Coupon;

/// <summary>
/// Applies a coupon to the customer's cart.
///
/// The coupon code is stored on the cart; the discount is never stored. Every subsequent
/// pricing pass re-validates the code, so a coupon that later expires, exhausts its usage
/// limit or stops matching the cart simply stops discounting instead of carrying a stale
/// amount forward. Usage is recorded only when an order is actually placed.
///
/// Returns the fully recalculated checkout summary, not just the discount, so the client
/// never has to derive the new total itself.
/// </summary>
public sealed record ApplyCouponCommand(Guid CustomerId, string CouponCode)
    : ICommand<CheckoutSummaryResponse>;

/// <summary>
/// Removes the coupon from the customer's cart. Idempotent — removing with no coupon applied
/// succeeds and returns the recalculated summary without a discount.
/// </summary>
public sealed record RemoveCouponCommand(Guid CustomerId) : ICommand<CheckoutSummaryResponse>;

internal sealed class ApplyCouponValidator : AbstractValidator<ApplyCouponCommand>
{
    public ApplyCouponValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("Customer is required.");

        RuleFor(x => x.CouponCode)
            .NotEmpty().WithMessage("Coupon code is required.")
            .MinimumLength(3).WithMessage("Coupon code must be at least 3 characters.")
            .MaximumLength(50).WithMessage("Coupon code must not exceed 50 characters.");
    }
}

internal sealed class RemoveCouponValidator : AbstractValidator<RemoveCouponCommand>
{
    public RemoveCouponValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("Customer is required.");
    }
}

internal sealed class ApplyCouponHandler(
    IApplicationDbContext db,
    ICheckoutSummaryService summaryService,
    ILogger<ApplyCouponHandler> logger)
    : ICommandHandler<ApplyCouponCommand, CheckoutSummaryResponse>
{
    public async Task<Result<CheckoutSummaryResponse>> Handle(
        ApplyCouponCommand command, CancellationToken cancellationToken)
    {
        var cart = await db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(
                c => c.CustomerId == command.CustomerId && c.ExpiresAt > DateTime.UtcNow,
                cancellationToken);

        if (cart is null || cart.Items.Count == 0)
            return Result.Failure<CheckoutSummaryResponse>(
                Error.Validation("CART_EMPTY", "Your cart is empty."));

        cart.ApplyCoupon(command.CouponCode);
        await db.SaveChangesAsync(cancellationToken);

        // Re-price with the code now on the cart. The summary carries the coupon's verdict, so
        // an invalid code is reported through CouponErrorCode rather than by failing here —
        // but an invalid code must NOT be left on the cart, otherwise it would be silently
        // ignored on the next request.
        var summary = await summaryService.CalculateAsync(
            new CheckoutSummaryRequest(command.CustomerId, PaymentMethod: null), cancellationToken);

        if (!summary.IsSuccess)
            return Result.Failure<CheckoutSummaryResponse>(summary.Error);

        if (summary.Value.CouponErrorCode is not null)
        {
            // Reject loudly and roll the cart back to its previous coupon state.
            cart.RemoveCoupon();
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Coupon {CouponCode} rejected for customer {CustomerId}: {ErrorCode}",
                command.CouponCode, command.CustomerId, summary.Value.CouponErrorCode);

            return Result.Failure<CheckoutSummaryResponse>(Error.Validation(
                summary.Value.CouponErrorCode,
                summary.Value.CouponErrorMessage ?? "Coupon is not valid."));
        }

        logger.LogInformation(
            "Coupon {CouponCode} applied to cart {CartId} for customer {CustomerId}. Discount: {Discount}",
            summary.Value.AppliedCouponCode, cart.Id, command.CustomerId, summary.Value.DiscountAmount);

        return Result.Success(CheckoutSummaryMapper.Map(summary.Value));
    }
}

internal sealed class RemoveCouponHandler(
    IApplicationDbContext db,
    ICheckoutSummaryService summaryService,
    ILogger<RemoveCouponHandler> logger)
    : ICommandHandler<RemoveCouponCommand, CheckoutSummaryResponse>
{
    public async Task<Result<CheckoutSummaryResponse>> Handle(
        RemoveCouponCommand command, CancellationToken cancellationToken)
    {
        var cart = await db.Carts
            .FirstOrDefaultAsync(
                c => c.CustomerId == command.CustomerId && c.ExpiresAt > DateTime.UtcNow,
                cancellationToken);

        // Idempotent: a cart with no coupon, or no cart at all, is already in the target state.
        if (cart is not null && cart.CouponCode is not null)
        {
            logger.LogInformation(
                "Coupon {CouponCode} removed from cart {CartId} for customer {CustomerId}",
                cart.CouponCode, cart.Id, command.CustomerId);

            cart.RemoveCoupon();
            await db.SaveChangesAsync(cancellationToken);
        }

        var summary = await summaryService.CalculateAsync(
            new CheckoutSummaryRequest(command.CustomerId, PaymentMethod: null), cancellationToken);

        if (!summary.IsSuccess)
            return Result.Failure<CheckoutSummaryResponse>(summary.Error);

        return Result.Success(CheckoutSummaryMapper.Map(summary.Value));
    }
}
