using KromicCommerce.Application.Features.Auth.SendOtp;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.UnitTests.Application;

public sealed class SendOtpHandlerTests
{
    private static (SendOtpHandler Handler, Mock<IApplicationDbContext> Db, List<OtpRequest> Rows) Build(
        ISmsProviderFactory factory, List<OtpRequest>? existing = null,
        int cooldownSeconds = 60, int maxAttempts = 5, int length = SmsOtpDefaults.Length,
        Mock<IOtpService>? otpService = null)
    {
        var rows = existing ?? [];
        var db = new Mock<IApplicationDbContext>();
        db.Setup(d => d.OtpRequests).Returns(MockDbSet<OtpRequest>(rows));
        // A mocked DbSet discards Add, so mirror it into the list the test asserts on.
        db.Setup(d => d.OtpRequests.Add(It.IsAny<OtpRequest>())).Callback<OtpRequest>(rows.Add);
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        otpService ??= new Mock<IOtpService>();
        otpService.Setup(s => s.GenerateOtp(It.IsAny<int>()))
            .Returns((int len) => new string('1', len));
        otpService.Setup(s => s.HashOtp(It.IsAny<string>())).Returns("hashed");

        var handler = new SendOtpHandler(
            db.Object,
            otpService.Object,
            factory,
            SmsTestDoubles.Policy(
                expiryMinutes: 10,
                resendCooldownSeconds: cooldownSeconds,
                maxAttempts: maxAttempts,
                length: length),
            NullLogger<SendOtpHandler>.Instance);

        return (handler, db, rows);
    }

    [Fact]
    public async Task The_code_is_four_digits_by_default()
    {
        var otpService = new Mock<IOtpService>();
        var (handler, _, _) = Build(SmsTestDoubles.Configured(), otpService: otpService);

        await handler.Handle(
            new SendOtpCommand("9876543210", OtpPurpose.PhoneVerification, Guid.NewGuid()),
            CancellationToken.None);

        otpService.Verify(s => s.GenerateOtp(4), Times.Once);
        otpService.Verify(s => s.GenerateOtp(It.Is<int>(n => n != 4)), Times.Never);
    }

    [Fact]
    public async Task A_configured_length_overrides_the_default()
    {
        var otpService = new Mock<IOtpService>();
        var (handler, _, _) = Build(SmsTestDoubles.Configured(), length: 6, otpService: otpService);

        await handler.Handle(
            new SendOtpCommand("9876543210", OtpPurpose.PhoneVerification, Guid.NewGuid()),
            CancellationToken.None);

        otpService.Verify(s => s.GenerateOtp(6), Times.Once);
    }

    [Fact]
    public async Task Refuses_to_send_when_no_provider_is_configured()
    {
        var (handler, _, rows) = Build(SmsTestDoubles.NotConfigured());

        var result = await handler.Handle(
            new SendOtpCommand("9876543210", OtpPurpose.PhoneVerification, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SMS_NOT_CONFIGURED");
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Rejects_an_unusable_phone_number_before_touching_the_gateway()
    {
        var (handler, _, rows) = Build(SmsTestDoubles.Configured());

        var result = await handler.Handle(
            new SendOtpCommand("123", OtpPurpose.PhoneVerification, Guid.NewGuid()),
            CancellationToken.None);

        result.Error.Code.Should().Be("INVALID_PHONE_NUMBER");
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Stores_the_code_in_canonical_form_and_captures_the_attempt_ceiling()
    {
        var (handler, _, rows) = Build(SmsTestDoubles.Configured(), maxAttempts: 3);

        var result = await handler.Handle(
            new SendOtpCommand("+91 98765 43210", OtpPurpose.PhoneVerification, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rows.Should().ContainSingle();
        rows[0].PhoneNumber.Should().Be("+919876543210");
        rows[0].MaxAttempts.Should().Be(3);
        rows[0].OtpHash.Should().Be("hashed");
    }

    [Fact]
    public async Task Enforces_the_resend_cooldown()
    {
        var recent = OtpRequest.Create("+919876543210", "h", OtpPurpose.PhoneVerification,
            DateTime.UtcNow.AddMinutes(10), maxAttempts: 5);

        var (handler, _, rows) = Build(SmsTestDoubles.Configured(), [recent], cooldownSeconds: 60);

        var result = await handler.Handle(
            new SendOtpCommand("9876543210", OtpPurpose.PhoneVerification, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("OTP_COOLDOWN");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Cooldown_ignores_a_different_phone_or_purpose()
    {
        var otherPurpose = OtpRequest.Create("+919876543210", "h", OtpPurpose.Login,
            DateTime.UtcNow.AddMinutes(10));

        var (handler, _, _) = Build(SmsTestDoubles.Configured(), [otherPurpose]);

        var result = await handler.Handle(
            new SendOtpCommand("9876543210", OtpPurpose.PhoneVerification, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Cooldown_does_not_apply_once_the_previous_code_was_used()
    {
        // CreatedAt is stamped at creation, so "outside the window" is modelled the way it
        // actually happens in production: the earlier code was verified.
        var used = OtpRequest.Create("+919876543210", "h", OtpPurpose.PhoneVerification,
            DateTime.UtcNow.AddMinutes(10));
        used.MarkVerified();

        var (handler, _, _) = Build(SmsTestDoubles.Configured(), [used]);

        var result = await handler.Handle(
            new SendOtpCommand("9876543210", OtpPurpose.PhoneVerification, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    private static DbSet<T> MockDbSet<T>(List<T> data) where T : class
    {
        var queryable = data.AsQueryable();
        var mock = new Mock<DbSet<T>>();
        mock.As<IQueryable<T>>().Setup(m => m.Provider)
            .Returns(new TestAsyncQueryProvider<T>(queryable.Provider));
        mock.As<IQueryable<T>>().Setup(m => m.Expression).Returns(queryable.Expression);
        mock.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(queryable.ElementType);
        mock.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(queryable.GetEnumerator());
        mock.As<IAsyncEnumerable<T>>()
            .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
            .Returns(new TestAsyncEnumerator<T>(queryable.GetEnumerator()));
        return mock.Object;
    }
}
