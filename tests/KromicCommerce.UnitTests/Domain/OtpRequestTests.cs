namespace KromicCommerce.UnitTests.Domain;

public sealed class OtpRequestTests
{
    private static OtpRequest CreateFresh(int expiryMinutes = 10) =>
        OtpRequest.Create("+919876543210", "hash123", OtpPurpose.PhoneVerification,
            DateTime.UtcNow.AddMinutes(expiryMinutes));

    [Fact]
    public void New_otp_can_attempt()
    {
        var otp = CreateFresh();
        otp.CanAttempt().Should().BeTrue();
    }

    [Fact]
    public void Expired_otp_cannot_attempt()
    {
        var otp = OtpRequest.Create("+919876543210", "hash", OtpPurpose.PhoneVerification,
            DateTime.UtcNow.AddMinutes(-1));
        otp.CanAttempt().Should().BeFalse();
        otp.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void Exhausted_after_max_attempts()
    {
        var otp = CreateFresh();
        for (var i = 0; i < OtpRequest.MaxAttempts - 1; i++)
            otp.RecordAttempt();

        otp.IsExhausted.Should().BeFalse();
        otp.CanAttempt().Should().BeTrue();

        var exhausted = otp.RecordAttempt();
        exhausted.Should().BeTrue();
        otp.IsExhausted.Should().BeTrue();
        otp.CanAttempt().Should().BeFalse();
    }

    [Fact]
    public void Verified_otp_cannot_attempt()
    {
        var otp = CreateFresh();
        otp.MarkVerified();
        otp.IsVerified.Should().BeTrue();
        otp.CanAttempt().Should().BeFalse();
    }
}
