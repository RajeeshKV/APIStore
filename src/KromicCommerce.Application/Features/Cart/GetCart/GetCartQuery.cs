namespace KromicCommerce.Application.Features.Cart.GetCart;

/// <summary>
/// Returns the cart for the current user (authenticated) or anonymous session.
/// AnonymousCartId is a server-issued secure token — never the customer ID.
/// </summary>
public sealed record GetCartQuery(
    Guid? CustomerId,
    string? AnonymousCartId) : IQuery<CartResponse>;
