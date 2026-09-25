namespace KromicCommerce.Application.Features.Cart.AddCartItem;

internal sealed class AddCartItemValidator : AbstractValidator<AddCartItemCommand>
{
    // Matches the server-generated token length: 32 bytes → 43 base64url chars.
    // Accepting up to 64 chars gives headroom for future format changes while
    // rejecting clearly malicious oversized values before any DB lookup.
    private const int MaxCartTokenLength = 64;

    public AddCartItemValidator()
    {
        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be > 0.")
            .LessThanOrEqualTo(100).WithMessage("Quantity must not exceed 100 per item.");

        RuleFor(x => x.AnonymousCartId)
            .MaximumLength(MaxCartTokenLength)
            .WithMessage($"Cart token must not exceed {MaxCartTokenLength} characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.AnonymousCartId));
    }
}
