namespace KromicCommerce.Domain.Orders;

/// <summary>
/// Customer shipping address captured at order time.
/// Stored as an owned entity on the Order so it is historically stable
/// even if the customer later changes their address.
/// No country-specific logic is embedded here — validation lives in the
/// Application validator so different stores can apply different rules.
/// </summary>
public sealed class ShippingAddress : ValueObject
{
    private ShippingAddress() { }

    public static ShippingAddress Create(
        string fullName,
        string phone,
        string addressLine1,
        string? addressLine2,
        string city,
        string state,
        string postalCode,
        string country)
    {
        if (string.IsNullOrWhiteSpace(fullName))   throw new ArgumentException("Full name is required.", nameof(fullName));
        if (string.IsNullOrWhiteSpace(phone))       throw new ArgumentException("Phone is required.", nameof(phone));
        if (string.IsNullOrWhiteSpace(addressLine1)) throw new ArgumentException("Address line 1 is required.", nameof(addressLine1));
        if (string.IsNullOrWhiteSpace(city))        throw new ArgumentException("City is required.", nameof(city));
        if (string.IsNullOrWhiteSpace(state))       throw new ArgumentException("State is required.", nameof(state));
        if (string.IsNullOrWhiteSpace(postalCode))  throw new ArgumentException("Postal code is required.", nameof(postalCode));
        if (string.IsNullOrWhiteSpace(country))     throw new ArgumentException("Country is required.", nameof(country));

        return new ShippingAddress
        {
            FullName = fullName.Trim(),
            Phone = phone.Trim(),
            AddressLine1 = addressLine1.Trim(),
            AddressLine2 = addressLine2?.Trim(),
            City = city.Trim(),
            State = state.Trim(),
            PostalCode = postalCode.Trim(),
            Country = country.Trim().ToUpperInvariant()
        };
    }

    public string FullName { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string AddressLine1 { get; private set; } = string.Empty;
    public string? AddressLine2 { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string PostalCode { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FullName;
        yield return Phone;
        yield return AddressLine1;
        yield return AddressLine2;
        yield return City;
        yield return State;
        yield return PostalCode;
        yield return Country;
    }
}
