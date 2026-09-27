namespace KromicCommerce.Application.Features.Auth.LoginWithEmail;

/// <summary>
/// Validates the admin login command.
/// The <see cref="LoginWithEmailCommand.Identifier"/> field accepts either an email address
/// or a username, so only non-empty + length constraints are applied here.
/// Credential correctness is verified by the handler, not the validator.
/// </summary>
internal sealed class LoginWithEmailValidator : AbstractValidator<LoginWithEmailCommand>
{
    public LoginWithEmailValidator()
    {
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Email or username is required.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(128);
    }
}
