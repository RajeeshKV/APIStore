namespace KromicCommerce.Application.Features.Cart.AddCartItem;

public sealed record AddCartItemCommand(
    Guid? CustomerId,
    string? AnonymousCartId,
    Guid ProductId,
    Guid? VariantId,
    int Quantity) : ICommand<CartResponse>;
