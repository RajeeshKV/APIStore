namespace KromicCommerce.Application.Features.Cart.ClearCart;

public sealed record ClearCartCommand(Guid? CustomerId, string? AnonymousCartId) : ICommand;
