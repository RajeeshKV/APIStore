namespace KromicCommerce.UnitTests.Domain;

public sealed class CustomerAddressTests
{
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static CustomerAddress Build(
        string? label = "Home",
        string firstName = "Jane",
        string lastName = "Doe",
        string? company = null,
        string addressLine1 = "123 Main St",
        string? addressLine2 = null,
        string city = "Mumbai",
        string state = "MH",
        string postalCode = "400001",
        string countryCode = "IN",
        string? phone = "+919876543210")
        => CustomerAddress.Create(
            CustomerId, label, firstName, lastName, company,
            addressLine1, addressLine2, city, state, postalCode, countryCode, phone);

    // -----------------------------------------------------------------------
    // Create
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_sets_all_fields_correctly()
    {
        var addr = Build();

        addr.CustomerId.Should().Be(CustomerId);
        addr.Label.Should().Be("Home");
        addr.FirstName.Should().Be("Jane");
        addr.LastName.Should().Be("Doe");
        addr.AddressLine1.Should().Be("123 Main St");
        addr.City.Should().Be("Mumbai");
        addr.State.Should().Be("MH");
        addr.PostalCode.Should().Be("400001");
        addr.CountryCode.Should().Be("IN");
        addr.Phone.Should().Be("+919876543210");
        addr.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void Create_uppercases_country_code()
    {
        var addr = Build(countryCode: "in");
        addr.CountryCode.Should().Be("IN");
    }

    [Fact]
    public void Create_trims_whitespace_on_fields()
    {
        var addr = Build(firstName: "  Jane  ", addressLine1: "  123 Main St  ", city: "  Mumbai  ");
        addr.FirstName.Should().Be("Jane");
        addr.AddressLine1.Should().Be("123 Main St");
        addr.City.Should().Be("Mumbai");
    }

    [Fact]
    public void Create_raises_CustomerAddressCreatedEvent()
    {
        var addr = Build();
        addr.DomainEvents.Should().ContainSingle(e => e is CustomerAddressCreatedEvent);
        var evt = (CustomerAddressCreatedEvent)addr.DomainEvents.First();
        evt.CustomerId.Should().Be(CustomerId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_for_empty_first_name(string name)
    {
        var act = () => Build(firstName: name);
        act.Should().Throw<ArgumentException>().WithMessage("*First name*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_for_empty_last_name(string name)
    {
        var act = () => Build(lastName: name);
        act.Should().Throw<ArgumentException>().WithMessage("*Last name*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_throws_for_empty_address_line1(string line)
    {
        var act = () => Build(addressLine1: line);
        act.Should().Throw<ArgumentException>().WithMessage("*Address line 1*");
    }

    [Theory]
    [InlineData("A")]       // 1 char
    [InlineData("ABC")]     // 3 chars
    [InlineData("")]        // empty
    [InlineData("   ")]     // whitespace
    public void Create_throws_for_invalid_country_code(string code)
    {
        var act = () => Build(countryCode: code);
        act.Should().Throw<ArgumentException>().WithMessage("*2-letter*");
    }

    [Fact]
    public void Create_allows_null_optional_fields()
    {
        var addr = Build(label: null, company: null, addressLine2: null, phone: null);
        addr.Label.Should().BeNull();
        addr.Company.Should().BeNull();
        addr.AddressLine2.Should().BeNull();
        addr.Phone.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // FullName
    // -----------------------------------------------------------------------

    [Fact]
    public void FullName_concatenates_first_and_last()
    {
        var addr = Build(firstName: "Jane", lastName: "Doe");
        addr.FullName.Should().Be("Jane Doe");
    }

    // -----------------------------------------------------------------------
    // SetAsDefault / ClearDefault
    // -----------------------------------------------------------------------

    [Fact]
    public void SetAsDefault_marks_address_as_default()
    {
        var addr = Build();
        addr.IsDefault.Should().BeFalse();

        addr.SetAsDefault();

        addr.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void SetAsDefault_raises_DefaultAddressChangedEvent()
    {
        var addr = Build();
        addr.ClearDomainEvents();

        addr.SetAsDefault();

        addr.DomainEvents.Should().ContainSingle(e => e is DefaultAddressChangedEvent);
        var evt = (DefaultAddressChangedEvent)addr.DomainEvents.First();
        evt.CustomerId.Should().Be(CustomerId);
        evt.NewDefaultAddressId.Should().Be(addr.Id);
    }

    [Fact]
    public void ClearDefault_unmarks_address()
    {
        var addr = Build();
        addr.SetAsDefault();
        addr.ClearDefault();

        addr.IsDefault.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Update
    // -----------------------------------------------------------------------

    [Fact]
    public void Update_replaces_all_fields()
    {
        var addr = Build();
        addr.Update("Work", "John", "Smith", "Acme", "456 Park Ave", "Suite 10",
            "Delhi", "DL", "110001", "US", "+918888888888");

        addr.Label.Should().Be("Work");
        addr.FirstName.Should().Be("John");
        addr.LastName.Should().Be("Smith");
        addr.Company.Should().Be("Acme");
        addr.AddressLine1.Should().Be("456 Park Ave");
        addr.AddressLine2.Should().Be("Suite 10");
        addr.City.Should().Be("Delhi");
        addr.State.Should().Be("DL");
        addr.PostalCode.Should().Be("110001");
        addr.CountryCode.Should().Be("US");
        addr.Phone.Should().Be("+918888888888");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_throws_for_empty_first_name(string name)
    {
        var addr = Build();
        var act = () => addr.Update(null, name, "Doe", null, "123 St", null, "City", "ST", "12345", "IN", null);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("A")]
    [InlineData("ABC")]
    public void Update_throws_for_invalid_country_code(string code)
    {
        var addr = Build();
        var act = () => addr.Update(null, "Jane", "Doe", null, "123 St", null, "City", "ST", "12345", code, null);
        act.Should().Throw<ArgumentException>().WithMessage("*2-letter*");
    }

    [Fact]
    public void Update_does_not_change_default_flag()
    {
        var addr = Build();
        addr.SetAsDefault();
        addr.Update("Work", "John", "Smith", null, "456 Ave", null, "Delhi", "DL", "110001", "IN", null);
        addr.IsDefault.Should().BeTrue();
    }
}
