namespace KromicCommerce.Application.Features.Auth.VerifyOtp;

internal sealed class VerifyOtpValidator : AbstractValidator<VerifyOtpCommand>
{
    public VerifyOtpValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .Matches(@"^\+?[1-9]\d{6,14}$");

        RuleFor(x => x.SubmittedOtp)
            .NotEmpty()
            .Matches(@"^\d{4,8}$").WithMessage("OTP must be 4-8 digits.");
    }
}
