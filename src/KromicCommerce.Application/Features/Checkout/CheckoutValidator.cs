namespace KromicCommerce.Application.Features.Checkout;

internal sealed class CheckoutValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutValidator()
    {
        RuleFor(x => x.PaymentMethod)
            .IsInEnum()
            .WithMessage("Payment method must be a valid PaymentMethod value.");

        RuleFor(x => x.AddressId)
            .NotEmpty().WithMessage("A saved address is required.");

        RuleFor(x => x.IdempotencyKey).MaximumLength(128).When(x => x.IdempotencyKey is not null);
    }
}
