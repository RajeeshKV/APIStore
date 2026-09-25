namespace KromicCommerce.Application.Features.Store.UpdateAuthSettings;

internal sealed class UpdateAuthSettingsValidator : AbstractValidator<UpdateAuthSettingsCommand>
{
    public UpdateAuthSettingsValidator()
    {
        RuleFor(x => x.OtpExpiryMinutes)
            .GreaterThanOrEqualTo(1).WithMessage("OTP expiry must be at least 1 minute.");

        RuleFor(x => x.OtpResendCooldownSeconds)
            .GreaterThanOrEqualTo(0).WithMessage("OTP resend cooldown must be >= 0.");

        RuleFor(x => x.OtpMaxAttempts)
            .GreaterThanOrEqualTo(1).WithMessage("OTP max attempts must be at least 1.");

        RuleFor(x => x.SmsProvider)
            .NotEmpty().WithMessage("SMS provider must not be empty.")
            .MaximumLength(100);

        // At least one auth method must be enabled
        RuleFor(x => x)
            .Must(x => x.EmailPasswordEnabled || x.MobileOtpEnabled || x.GoogleOAuthEnabled)
            .WithMessage("At least one authentication method must be enabled.")
            .WithName("AuthMethods");
    }
}
