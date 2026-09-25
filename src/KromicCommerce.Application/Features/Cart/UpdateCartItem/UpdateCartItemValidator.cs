namespace KromicCommerce.Application.Features.Cart.UpdateCartItem;

internal sealed class UpdateCartItemValidator : AbstractValidator<UpdateCartItemCommand>
{
    public UpdateCartItemValidator()
    {
        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be > 0.")
            .LessThanOrEqualTo(100);
    }
}
