namespace KromicCommerce.Contracts.Cart;

public sealed record AddCartItemRequest(
    Guid ProductId,
    Guid? VariantId,
    int Quantity);
