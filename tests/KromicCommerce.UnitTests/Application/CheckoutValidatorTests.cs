using KromicCommerce.Application.Features.Checkout;

namespace KromicCommerce.UnitTests.Application;

public sealed class CheckoutValidatorTests
{
    private readonly CheckoutValidator _validator = new();

    private static ShippingAddressDto ValidAddress() =>
        new("Jane Doe", "+919876543210", "123 Street", null,
            "Mumbai", "Maharashtra", "400001", "IN");

    private static CheckoutCommand ValidCmd() =>
        new(Guid.NewGuid(), ValidAddress(), "Razorpay", null, null);

    [Fact]
    public void Valid_command_passes()
    {
        _validator.Validate(ValidCmd()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bitcoin")]
    [InlineData("PayPal")]
    public void Invalid_payment_method_fails(string method)
    {
        var result = _validator.Validate(ValidCmd() with { PaymentMethod = method });
        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Razorpay")]
    [InlineData("CashOnDelivery")]
    public void Valid_payment_methods_pass(string method)
    {
        var result = _validator.Validate(ValidCmd() with { PaymentMethod = method });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_full_name_fails()
    {
        var addr = ValidAddress() with { FullName = "" };
        _validator.Validate(ValidCmd() with { ShippingAddress = addr }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Invalid_phone_fails()
    {
        var addr = ValidAddress() with { Phone = "abc" };
        _validator.Validate(ValidCmd() with { ShippingAddress = addr }).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("IN")]
    [InlineData("US")]
    [InlineData("GB")]
    public void Two_letter_country_passes(string country)
    {
        var addr = ValidAddress() with { Country = country };
        _validator.Validate(ValidCmd() with { ShippingAddress = addr }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("India")]
    [InlineData("I")]
    [InlineData("")]
    public void Invalid_country_fails(string country)
    {
        var addr = ValidAddress() with { Country = country };
        _validator.Validate(ValidCmd() with { ShippingAddress = addr }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void COD_payment_method_is_valid()
    {
        var result = _validator.Validate(ValidCmd() with { PaymentMethod = "CashOnDelivery" });
        result.IsValid.Should().BeTrue();
    }
}
