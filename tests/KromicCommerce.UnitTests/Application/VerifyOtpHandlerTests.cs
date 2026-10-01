using KromicCommerce.Application.Features.Auth.SendOtp;
using KromicCommerce.Application.Features.Auth.VerifyOtp;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// Covers the phone-state effects of a successful OTP verification.
///
/// The rule under test: proving control of a number is not the same as designating it the account
/// phone. Only a <see cref="OtpPurpose.PhoneVerification"/> code that matches the number currently
/// being verified may change the profile; everything else must leave phone state untouched.
/// </summary>
public sealed class VerifyOtpHandlerTests
{
    private const string VerifiedPhone = "+919876543210";
    private const string AbandonedPhone = "+918888888888";
    private const string CurrentPhone = "+917777777777";

    // ---------------------------------------------------------------------------------------------
    // The regression: a stale OTP must not promote the phone it was sent to.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_otp_for_an_abandoned_change_is_refused_and_changes_nothing()
    {
        // 1. Verified phone A.
        var user = VerifiedUser();

        // 2-3. A change to B is requested and an OTP is issued for B.
        user.RequestPhoneNumberChange(AbandonedPhone);
        var (handler, otps) = Build(user, otpPhone: AbandonedPhone);

        // 4-5. Before that code is used, the customer requests a change to C.
        user.RequestPhoneNumberChange(CurrentPhone);

        // 6-7. The still-valid OTP for B is submitted.
        var result = await handler.Handle(
            new VerifyOtpCommand(AbandonedPhone, "1234", OtpPurpose.PhoneVerification, user.Id),
            CancellationToken.None);

        // 8. Refused.
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PHONE_VERIFICATION_NOT_PENDING");

        // 9-11. A stays verified, C stays pending, and B is never promoted.
        user.PhoneNumber.Should().Be(VerifiedPhone);
        user.PhoneNumberVerified.Should().BeTrue();
        user.PendingPhoneNumber.Should().Be(CurrentPhone);
    }

    [Fact]
    public async Task A_refused_stale_otp_is_not_marked_as_used()
    {
        var user = VerifiedUser();
        user.RequestPhoneNumberChange(AbandonedPhone);
        var (handler, otps) = Build(user, otpPhone: AbandonedPhone);
        user.RequestPhoneNumberChange(CurrentPhone);

        await handler.Handle(
            new VerifyOtpCommand(AbandonedPhone, "1234", OtpPurpose.PhoneVerification, user.Id),
            CancellationToken.None);

        // The code is refused outright, not spent as though it had been accepted. The attempt it
        // costs sits on the abandoned number's own row, so it cannot weaken the attempt budget for
        // the number the customer is actually verifying.
        otps.Single().VerifiedAt.Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------------
    // PhoneVerification — the one purpose allowed to move phone state.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_matching_phone_verification_promotes_the_pending_number()
    {
        var user = VerifiedUser();
        user.RequestPhoneNumberChange(CurrentPhone);
        var (handler, _) = Build(user, otpPhone: CurrentPhone);

        var result = await handler.Handle(
            new VerifyOtpCommand(CurrentPhone, "1234", OtpPurpose.PhoneVerification, user.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PhoneNumber.Should().Be(CurrentPhone);
        user.PhoneNumberVerified.Should().BeTrue();
        user.PendingPhoneNumber.Should().BeNull();
    }

    [Fact]
    public async Task The_registration_number_can_be_verified_with_nothing_pending()
    {
        // Registration sets the phone directly and leaves it unverified, so there is no pending
        // value to match. That first verification must still work.
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.SetPhoneNumber(VerifiedPhone, verified: false);
        var (handler, _) = Build(user, otpPhone: VerifiedPhone);

        var result = await handler.Handle(
            new VerifyOtpCommand(VerifiedPhone, "1234", OtpPurpose.PhoneVerification, user.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PhoneNumber.Should().Be(VerifiedPhone);
        user.PhoneNumberVerified.Should().BeTrue();
    }

    [Fact]
    public async Task A_wrong_code_never_changes_phone_state()
    {
        var user = VerifiedUser();
        user.RequestPhoneNumberChange(CurrentPhone);
        var (handler, _) = Build(user, otpPhone: CurrentPhone, acceptOtp: false);

        var result = await handler.Handle(
            new VerifyOtpCommand(CurrentPhone, "9999", OtpPurpose.PhoneVerification, user.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("OTP_INVALID");
        user.PhoneNumber.Should().Be(VerifiedPhone);
        user.PendingPhoneNumber.Should().Be(CurrentPhone);
        user.PhoneNumberVerified.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------------
    // Login / PasswordReset — must complete their own operation and nothing else.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(OtpPurpose.Login)]
    [InlineData(OtpPurpose.PasswordReset)]
    public async Task Other_purposes_complete_without_promoting_the_pending_number(OtpPurpose purpose)
    {
        // Proving control of a number for a login or a password reset is not a statement about
        // which number belongs on the account. Promoting here would let an unrelated flow silently
        // replace the customer's phone.
        var user = VerifiedUser();
        user.RequestPhoneNumberChange(CurrentPhone);
        var (handler, otps) = Build(user, otpPhone: CurrentPhone, otpPurpose: purpose);

        var result = await handler.Handle(
            new VerifyOtpCommand(CurrentPhone, "1234", purpose, user.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue("the code itself was valid and its own operation proceeds");

        // No phone state moved.
        user.PhoneNumber.Should().Be(VerifiedPhone);
        user.PhoneNumberVerified.Should().BeTrue();
        user.PendingPhoneNumber.Should().Be(CurrentPhone);

        // The code is still spent, so it cannot be replayed.
        otps.Single().VerifiedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(OtpPurpose.Login)]
    [InlineData(OtpPurpose.PasswordReset)]
    public async Task Other_purposes_cannot_overwrite_a_verified_number(OtpPurpose purpose)
    {
        var user = VerifiedUser();
        var (handler, _) = Build(user, otpPhone: AbandonedPhone, otpPurpose: purpose);

        var result = await handler.Handle(
            new VerifyOtpCommand(AbandonedPhone, "1234", purpose, user.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PhoneNumber.Should().Be(VerifiedPhone, "an unrelated purpose must never rewrite the profile phone");
        user.PhoneNumberVerified.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------------
    // Scoping.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_anonymous_phone_verification_succeeds_without_touching_a_user()
    {
        // Pre-login flows verify without a user id. There is no profile to promote, so this must
        // succeed rather than fail looking for a user.
        var (handler, _) = Build(user: null, otpPhone: VerifiedPhone);

        var result = await handler.Handle(
            new VerifyOtpCommand(VerifiedPhone, "1234", OtpPurpose.PhoneVerification, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_code_for_another_purpose_is_not_accepted()
    {
        // Purpose scoping is unchanged: a Login code cannot be spent as a phone verification.
        var user = VerifiedUser();
        user.RequestPhoneNumberChange(CurrentPhone);
        var (handler, _) = Build(user, otpPhone: CurrentPhone, otpPurpose: OtpPurpose.Login);

        var result = await handler.Handle(
            new VerifyOtpCommand(CurrentPhone, "1234", OtpPurpose.PhoneVerification, user.Id),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("OTP_INVALID");
        user.PhoneNumber.Should().Be(VerifiedPhone);
        user.PendingPhoneNumber.Should().Be(CurrentPhone);
    }

    [Fact]
    public async Task The_phone_is_matched_canonically_not_as_typed()
    {
        // The stored numbers are canonical E.164, and a client may echo back any common format.
        // Comparing raw strings would reject a legitimate verification.
        var user = VerifiedUser();
        user.RequestPhoneNumberChange(CurrentPhone);
        var (handler, _) = Build(user, otpPhone: CurrentPhone);

        var result = await handler.Handle(
            new VerifyOtpCommand("+91 77777 77777", "1234", OtpPurpose.PhoneVerification, user.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PhoneNumber.Should().Be(CurrentPhone);
        user.PhoneNumberVerified.Should().BeTrue();
    }

    private static User VerifiedUser()
    {
        var user = User.CreateCustomer("a@b.com", null, "A", "B");
        user.SetPhoneNumber(VerifiedPhone, verified: true);
        return user;
    }

    private static (VerifyOtpHandler Handler, List<OtpRequest> Otps) Build(
        User? user, string otpPhone, bool acceptOtp = true,
        OtpPurpose otpPurpose = OtpPurpose.PhoneVerification)
    {
        var otp = OtpRequest.Create(
            otpPhone, "hash:1234", otpPurpose,
            DateTime.UtcNow.AddMinutes(10), user?.Id, maxAttempts: 5);

        var rows = new List<OtpRequest> { otp };
        var db = new Mock<IApplicationDbContext>();
        db.Setup(d => d.OtpRequests).Returns(MockDbSet(rows));
        db.Setup(d => d.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        if (user is not null)
        {
            db.Setup(d => d.Users).Returns(MockDbSet(new List<User> { user }));
        }

        var otpService = new Mock<IOtpService>();
        otpService.Setup(s => s.VerifyOtp(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(acceptOtp);

        return (new VerifyOtpHandler(db.Object, otpService.Object, NullLogger<VerifyOtpHandler>.Instance), rows);
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