namespace KromicCommerce.Application.Features.Auth.RegisterCustomer;

public sealed record RegisterCustomerCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? DeviceHint) : ICommand<TokenResponse>;
