namespace KromicCommerce.Contracts.Orders;

public sealed record ShippingAddressDto(
    string FullName,
    string Phone,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string Country);
