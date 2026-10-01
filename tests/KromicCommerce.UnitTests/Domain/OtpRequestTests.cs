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
        for (var i = 0; i < otp.MaxAttempts - 1; i++)
            otp.RecordAttempt();

        otp.IsExhausted.Should().BeFalse();
        otp.CanAttempt().Should().BeTrue();

        var exhausted = otp.RecordAttempt();
        exhausted.Should().BeTrue();
        otp.IsExhausted.Should().BeTrue();
        otp.CanAttempt().Should().BeFalse();
    }

    [Fact]
    public void Custom_max_attempts_is_captured_on_the_request()
    {
        var otp = OtpRequest.Create("+919876543210", "hash", OtpPurpose.Login,
            DateTime.UtcNow.AddMinutes(10), userId: null, maxAttempts: 3);

        otp.MaxAttempts.Should().Be(3);

        otp.RecordAttempt();
        otp.RecordAttempt();
        otp.IsExhausted.Should().BeFalse();

        otp.RecordAttempt();
        otp.IsExhausted.Should().BeTrue();
    }

    [Fact]
    public void Create_rejects_a_non_positive_attempt_ceiling()
    {
        var act = () => OtpRequest.Create("+919876543210", "hash", OtpPurpose.Login,
            DateTime.UtcNow.AddMinutes(10), userId: null, maxAttempts: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
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
