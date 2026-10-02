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

        // SmsProvider is optional. It was previously required, which made this endpoint reject the
        // obvious auth-settings payload with "The SmsProvider field is required" for a value that
        // has no effect on delivery: the active gateway is chosen through
        // PUT /api/v1/admin/integrations/sms and read from the SmsProviderConfig row. Requiring it
        // forced admins to state a second, competing answer to a question the backend asks
        // elsewhere — one that could silently disagree with the provider actually in use.
        RuleFor(x => x.SmsProvider)
            .MaximumLength(100)
            .When(x => x.SmsProvider is not null);

        // At least one auth method must be enabled
        RuleFor(x => x)
            .Must(x => x.EmailPasswordEnabled || x.MobileOtpEnabled || x.GoogleOAuthEnabled)
            .WithMessage("At least one authentication method must be enabled.")
            .WithName("AuthMethods");
    }
}
