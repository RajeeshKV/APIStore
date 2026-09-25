namespace KromicCommerce.Application.Features.Auth.LoginWithEmail;

internal sealed class LoginWithEmailValidator : AbstractValidator<LoginWithEmailCommand>
{
    public LoginWithEmailValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}
