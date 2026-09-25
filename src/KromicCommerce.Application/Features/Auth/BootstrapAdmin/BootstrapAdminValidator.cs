namespace KromicCommerce.Application.Features.Auth.BootstrapAdmin;

internal sealed class BootstrapAdminValidator : AbstractValidator<BootstrapAdminCommand>
{
    public BootstrapAdminValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(10).WithMessage("Password must be at least 10 characters.")
            .MaximumLength(128);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BootstrapSecret).NotEmpty();
        RuleFor(x => x.Username)
            .MinimumLength(3).WithMessage("Username must be at least 3 characters.")
            .MaximumLength(50)
            .When(x => !string.IsNullOrWhiteSpace(x.Username));
    }
}
