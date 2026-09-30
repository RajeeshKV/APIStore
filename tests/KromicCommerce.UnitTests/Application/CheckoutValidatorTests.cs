using KromicCommerce.Application.Features.Checkout;
using KromicCommerce.Domain.Orders;

namespace KromicCommerce.UnitTests.Application;

public sealed class CheckoutValidatorTests
{
    private readonly CheckoutValidator _validator = new();

    private static CheckoutCommand ValidCmd() =>
        new(Guid.NewGuid(), Guid.NewGuid(), PaymentMethod.Razorpay, null, null);

    [Fact]
    public void Valid_command_passes()
    {
        _validator.Validate(ValidCmd()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(PaymentMethod.Razorpay)]
    [InlineData(PaymentMethod.CashOnDelivery)]
    public void Valid_payment_methods_pass(PaymentMethod method)
    {
        var result = _validator.Validate(ValidCmd() with { PaymentMethod = method });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_address_id_fails()
    {
        _validator.Validate(ValidCmd() with { AddressId = Guid.Empty }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void COD_payment_method_is_valid()
    {
        var result = _validator.Validate(ValidCmd() with { PaymentMethod = PaymentMethod.CashOnDelivery });
        result.IsValid.Should().BeTrue();
    }
}
