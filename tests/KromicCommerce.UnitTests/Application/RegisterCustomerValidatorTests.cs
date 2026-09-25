using KromicCommerce.Application.Features.Auth.RegisterCustomer;

namespace KromicCommerce.UnitTests.Application;

public sealed class RegisterCustomerValidatorTests
{
    private readonly RegisterCustomerValidator _validator = new();

    private static RegisterCustomerCommand Valid() =>
        new("jane@example.com", "Password123!", "Jane", "Doe", null, null);

    [Fact]
    public void Valid_command_passes()
    {
        var result = _validator.Validate(Valid());
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    public void Invalid_email_fails(string email)
    {
        var result = _validator.Validate(Valid() with { Email = email });
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]        // < 8 chars
    public void Invalid_password_fails(string password)
    {
        var result = _validator.Validate(Valid() with { Password = password });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_first_name_fails()
    {
        var result = _validator.Validate(Valid() with { FirstName = "" });
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("123")]          // too short
    [InlineData("abc")]          // not digits
    [InlineData("+")]            // just the plus sign
    public void Invalid_phone_fails(string phone)
    {
        var result = _validator.Validate(Valid() with { PhoneNumber = phone });
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("+919876543210")]
    [InlineData("+12025551234")]
    public void Valid_phone_passes(string phone)
    {
        var result = _validator.Validate(Valid() with { PhoneNumber = phone });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Null_phone_passes_validation()
    {
        var result = _validator.Validate(Valid() with { PhoneNumber = null });
        result.IsValid.Should().BeTrue();
    }
}
