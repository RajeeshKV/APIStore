namespace KromicCommerce.Contracts.Me;

public sealed record UpdateAddressRequest(
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
    string? Phone);
