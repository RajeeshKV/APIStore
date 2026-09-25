namespace KromicCommerce.Application.Features.Me.Addresses;

public sealed record GetCustomerAddressesQuery(Guid CustomerId)
    : IQuery<IReadOnlyList<CustomerAddressResponse>>;

public sealed record GetCustomerAddressByIdQuery(Guid CustomerId, Guid AddressId)
    : IQuery<CustomerAddressResponse>;

public sealed record CreateCustomerAddressCommand(
    Guid CustomerId,
    string? Label,
    string FirstName,
    string LastName,
    string? Company,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode,
    string? Phone,
    bool IsDefault) : ICommand<CustomerAddressResponse>;

public sealed record UpdateCustomerAddressCommand(
    Guid CustomerId,
    Guid AddressId,
    string? Label,
    string FirstName,
    string LastName,
    string? Company,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string CountryCode,
    string? Phone) : ICommand<CustomerAddressResponse>;

public sealed record DeleteCustomerAddressCommand(Guid CustomerId, Guid AddressId) : ICommand;
public sealed record SetDefaultCustomerAddressCommand(Guid CustomerId, Guid AddressId) : ICommand;
