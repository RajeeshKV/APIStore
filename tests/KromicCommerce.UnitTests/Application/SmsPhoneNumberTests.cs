namespace KromicCommerce.UnitTests.Application;

public sealed class SmsPhoneNumberTests
{
    [Theory]
    [InlineData("+919876543210", "+919876543210")]
    [InlineData("919876543210", "+919876543210")]
    [InlineData("9876543210", "+919876543210")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("(98765) 43210", "+919876543210")]
    public void Canonicalises_common_formats(string input, string expected)
        => SmsPhoneNumber.TryToE164(input).Should().Be(expected);

    [Theory]
    [InlineData("1234567890")]   // Indian mobiles start 6-9
    [InlineData("5876543210")]
    [InlineData("0123456789")]
    [InlineData("98765")]        // too short
    [InlineData("98765432101234")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_anything_that_is_not_an_indian_mobile(string? input)
        => SmsPhoneNumber.TryToE164(input).Should().BeNull();

    [Fact]
    public void AreEquivalent_across_formats()
    {
        SmsPhoneNumber.AreEquivalent("+91 98765 43210", "9876543210").Should().BeTrue();
        SmsPhoneNumber.AreEquivalent("919876543210", "9999999999").Should().BeFalse();
        SmsPhoneNumber.AreEquivalent(null, "9876543210").Should().BeFalse();
    }

    [Fact]
    public void Mask_never_reveals_the_full_number()
    {
        SmsPhoneNumber.Mask("+919876543210").Should().Be("...3210");
        SmsPhoneNumber.Mask("12").Should().Be("****");
        SmsPhoneNumber.Mask(null).Should().Be("****");
    }

    [Fact]
    public void National_form_is_bare_ten_digits()
    {
        SmsPhoneNumber.TryToNational("+919876543210").Should().Be("9876543210");
        SmsPhoneNumber.TryToNational("+44 7700 900123").Should().BeNull();
    }
}
