namespace KromicCommerce.Application.Features.Checkout;

internal sealed class CheckoutValidator : AbstractValidator<CheckoutCommand>
{
    private static readonly string[] ValidPaymentMethods = ["Razorpay", "CashOnDelivery"];

    public CheckoutValidator()
    {
        RuleFor(x => x.PaymentMethod)
            .NotEmpty()
            .Must(m => ValidPaymentMethods.Contains(m))
            .WithMessage("Payment method must be 'Razorpay' or 'CashOnDelivery'.");

        RuleFor(x => x.ShippingAddress.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ShippingAddress.Phone)
            .NotEmpty().MaximumLength(20)
            .Matches(@"^\+?[1-9]\d{6,14}$").WithMessage("Phone must be a valid number.");
        RuleFor(x => x.ShippingAddress.AddressLine1).NotEmpty().MaximumLength(300);
        RuleFor(x => x.ShippingAddress.AddressLine2).MaximumLength(300)
            .When(x => x.ShippingAddress.AddressLine2 is not null);
        RuleFor(x => x.ShippingAddress.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShippingAddress.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShippingAddress.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.ShippingAddress.Country)
            .NotEmpty().Length(2).WithMessage("Country must be a 2-letter ISO code.");

        RuleFor(x => x.IdempotencyKey).MaximumLength(128).When(x => x.IdempotencyKey is not null);
    }
}
