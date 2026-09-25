using KromicCommerce.Application.Abstractions.Commerce;
using KromicCommerce.Contracts.Promotions;

namespace KromicCommerce.Application.Features.Store.Promotions;

// -----------------------------------------------------------------------
// Query
// -----------------------------------------------------------------------

public sealed record ValidateCouponQuery(
    string CouponCode,
    Guid CustomerId)
    : IQuery<CouponValidationResponse>;

// -----------------------------------------------------------------------
// Validator
// -----------------------------------------------------------------------

internal sealed class ValidateCouponValidator : AbstractValidator<ValidateCouponQuery>
{
    public ValidateCouponValidator()
    {
        RuleFor(x => x.CouponCode).NotEmpty().MinimumLength(3).MaximumLength(50);
        RuleFor(x => x.CustomerId).NotEmpty();
    }
}

// -----------------------------------------------------------------------
// Handler
// -----------------------------------------------------------------------

internal sealed class ValidateCouponHandler(
    IApplicationDbContext db,
    IPromotionService promotionService)
    : IQueryHandler<ValidateCouponQuery, CouponValidationResponse>
{
    public async Task<Result<CouponValidationResponse>> Handle(
        ValidateCouponQuery query, CancellationToken ct)
    {
        // Load the customer's current cart for context
        var cart = await db.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(
                c => c.CustomerId == query.CustomerId && c.ExpiresAt > DateTime.UtcNow, ct);

        if (cart is null || !cart.Items.Any())
            return Result.Success(new CouponValidationResponse(
                false, null, 0m, null, 0m,
                "CART_EMPTY", "Your cart is empty."));

        // Build cart item context — need category IDs
        var productIds = cart.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.CategoryId, p.Price })
            .ToListAsync(ct);

        var variantIds = cart.Items
            .Where(i => i.VariantId.HasValue)
            .Select(i => i.VariantId!.Value)
            .ToList();
        var variantPrices = variantIds.Any()
            ? await db.ProductVariants
                .Where(v => variantIds.Contains(v.Id))
                .Select(v => new { v.Id, v.PriceOverride })
                .ToListAsync(ct)
            : [];

        decimal subtotal = 0m;
        var cartItemContexts = new List<CartItemContext>();

        foreach (var ci in cart.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == ci.ProductId);
            if (product is null) continue;

            var priceOverride = ci.VariantId.HasValue
                ? variantPrices.FirstOrDefault(v => v.Id == ci.VariantId.Value)?.PriceOverride
                : null;

            var unitPrice = priceOverride ?? product.Price;
            var lineTotal = unitPrice * ci.Quantity;
            subtotal += lineTotal;

            cartItemContexts.Add(new CartItemContext(ci.ProductId, product.CategoryId, lineTotal));
        }

        // First-order check
        var isFirstOrder = !await db.Orders
            .AnyAsync(o => o.CustomerId == query.CustomerId, ct);

        var result = await promotionService.ValidateAndCalculateAsync(
            query.CouponCode, query.CustomerId, subtotal,
            cartItemContexts, isFirstOrder, ct);

        return Result.Success(new CouponValidationResponse(
            result.IsValid,
            result.CouponCode,
            result.DiscountAmount,
            result.DiscountType?.ToString(),
            result.EligibleSubtotal,
            result.ErrorCode,
            result.ErrorMessage));
    }
}
