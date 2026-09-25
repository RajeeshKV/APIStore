using KromicCommerce.Domain.Identity.Events;

namespace KromicCommerce.Domain.Identity;

/// <summary>
/// A saved delivery address for a customer account.
/// Customers may have multiple addresses; at most one may be marked as default.
/// Default address management is transactional — clearing the old default and
/// setting the new one must happen in the same SaveChangesAsync call.
///
/// When the default address is deleted, the most recently created remaining
/// address is promoted to default (if any remain). If no address remains,
/// the customer has no default — this is valid.
///
/// Ownership: always derived from ICurrentUserService, never trusted from the client.
/// </summary>
public sealed class CustomerAddress : AuditableEntity
{
    private CustomerAddress() { } // EF constructor

    public static CustomerAddress Create(
        Guid customerId,
        string? label,
        string firstName,
        string lastName,
        string? company,
        string addressLine1,
        string? addressLine2,
        string city,
        string state,
        string postalCode,
        string countryCode,
        string? phone)
    {
        ValidateRequired(firstName, nameof(firstName), "First name");
        ValidateRequired(lastName, nameof(lastName), "Last name");
        ValidateRequired(addressLine1, nameof(addressLine1), "Address line 1");
        ValidateRequired(city, nameof(city), "City");
        ValidateRequired(state, nameof(state), "State");
        ValidateRequired(postalCode, nameof(postalCode), "Postal code");
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
            throw new ArgumentException("Country code must be a 2-letter ISO 3166-1 alpha-2 code.", nameof(countryCode));

        var address = new CustomerAddress
        {
            CustomerId = customerId,
            Label = label?.Trim(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Company = company?.Trim(),
            AddressLine1 = addressLine1.Trim(),
            AddressLine2 = addressLine2?.Trim(),
            City = city.Trim(),
            State = state.Trim(),
            PostalCode = postalCode.Trim(),
            CountryCode = countryCode.Trim().ToUpperInvariant(),
            Phone = phone?.Trim(),
            IsDefault = false
        };
        address.RaiseDomainEvent(new CustomerAddressCreatedEvent(address.Id, customerId));
        return address;
    }

    public Guid CustomerId { get; private set; }

    /// <summary>Optional user-supplied label, e.g. "Home", "Work".</summary>
    public string? Label { get; private set; }

    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Company { get; private set; }
    public string AddressLine1 { get; private set; } = string.Empty;
    public string? AddressLine2 { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string PostalCode { get; private set; } = string.Empty;

    /// <summary>ISO 3166-1 alpha-2 (e.g. IN, US, GB).</summary>
    public string CountryCode { get; private set; } = string.Empty;

    public string? Phone { get; private set; }
    public bool IsDefault { get; private set; }

    public string FullName => $"{FirstName} {LastName}".Trim();

    // -----------------------------------------------------------------------
    // Behaviour
    // -----------------------------------------------------------------------

    public void Update(
        string? label,
        string firstName,
        string lastName,
        string? company,
        string addressLine1,
        string? addressLine2,
        string city,
        string state,
        string postalCode,
        string countryCode,
        string? phone)
    {
        ValidateRequired(firstName, nameof(firstName), "First name");
        ValidateRequired(lastName, nameof(lastName), "Last name");
        ValidateRequired(addressLine1, nameof(addressLine1), "Address line 1");
        ValidateRequired(city, nameof(city), "City");
        ValidateRequired(state, nameof(state), "State");
        ValidateRequired(postalCode, nameof(postalCode), "Postal code");
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
            throw new ArgumentException("Country code must be a 2-letter ISO 3166-1 alpha-2 code.", nameof(countryCode));

        Label = label?.Trim();
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Company = company?.Trim();
        AddressLine1 = addressLine1.Trim();
        AddressLine2 = addressLine2?.Trim();
        City = city.Trim();
        State = state.Trim();
        PostalCode = postalCode.Trim();
        CountryCode = countryCode.Trim().ToUpperInvariant();
        Phone = phone?.Trim();
    }

    /// <summary>Called by the handler that also clears other addresses' default flag.</summary>
    public void SetAsDefault()
    {
        IsDefault = true;
        RaiseDomainEvent(new DefaultAddressChangedEvent(CustomerId, Id));
    }

    public void ClearDefault() => IsDefault = false;

    // -----------------------------------------------------------------------
    // Guard
    // -----------------------------------------------------------------------

    private static void ValidateRequired(string? value, string paramName, string friendlyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{friendlyName} is required.", paramName);
    }
}
