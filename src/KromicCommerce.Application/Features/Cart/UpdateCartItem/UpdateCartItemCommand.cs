namespace KromicCommerce.Application.Features.Cart.UpdateCartItem;

public sealed record UpdateCartItemCommand(
    Guid? CustomerId,
    string? AnonymousCartId,
    Guid CartItemId,
    int Quantity) : ICommand<CartResponse>;
