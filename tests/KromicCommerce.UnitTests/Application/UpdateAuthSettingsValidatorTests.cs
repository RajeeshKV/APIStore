using KromicCommerce.Application.Features.Store.UpdateAuthSettings;

namespace KromicCommerce.UnitTests.Application;

/// <summary>
/// The auth settings screen and the SMS gateway screen answer related but different questions.
/// These tests pin the boundary so neither endpoint demands the other's field again.
/// </summary>
public sealed class UpdateAuthSettingsValidatorTests
{
    private static UpdateAuthSettingsCommand Valid(
        bool mobileOtp = true, string? smsProvider = null) =>
        new(GoogleOAuthEnabled: true,
            EmailPasswordEnabled: true,
            MobileOtpEnabled: mobileOtp,
            OtpExpiryMinutes: 10,
            OtpResendCooldownSeconds: 60,
            OtpMaxAttempts: 5,
            SmsProvider: smsProvider);

    [Fact]
    public void Otp_policy_saves_without_naming_a_provider()
    {
        // The exact payload an admin form sends when changing only the OTP timings. It used to be
        // rejected with "The SmsProvider field is required" — for a value that does not choose the
        // gateway. The gateway is selected through PUT /admin/integrations/sms.
        var result = new UpdateAuthSettingsValidator().Validate(Valid());

        result.IsValid.Should().BeTrue(
            "auth settings must not force a second, competing answer to which SMS gateway is active");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void An_older_client_that_still_sends_a_provider_is_accepted()
    {
        var result = new UpdateAuthSettingsValidator().Validate(Valid(smsProvider: "2Factor"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_absurdly_long_provider_name_is_still_rejected()
    {
        var result = new UpdateAuthSettingsValidator()
            .Validate(Valid(smsProvider: new string('p', 101)));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Otp_policy_bounds_are_still_enforced()
    {
        var tooFewAttempts = new UpdateAuthSettingsValidator().Validate(Valid() with
        {
            OtpMaxAttempts = 0
        });

        tooFewAttempts.IsValid.Should().BeFalse();

        var negativeCooldown = new UpdateAuthSettingsValidator().Validate(Valid() with
        {
            OtpResendCooldownSeconds = -1
        });

        negativeCooldown.IsValid.Should().BeFalse();
    }

    [Fact]
    public void At_least_one_sign_in_method_must_stay_enabled()
    {
        var result = new UpdateAuthSettingsValidator().Validate(
            Valid() with
            {
                GoogleOAuthEnabled = false,
                EmailPasswordEnabled = false,
                MobileOtpEnabled = false
            });

        result.IsValid.Should().BeFalse();
    }
}