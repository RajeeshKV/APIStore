namespace KromicCommerce.Application.Features.Store.UpdateEmailSettings;

internal sealed class UpdateEmailSettingsValidator : AbstractValidator<UpdateEmailSettingsCommand>
{
    private static readonly string[] ValidModes = ["KromicManaged", "CustomerBrevo"];

    public UpdateEmailSettingsValidator()
    {
        RuleFor(x => x.Mode)
            .NotEmpty()
            .Must(m => ValidModes.Contains(m))
            .WithMessage("Mode must be 'KromicManaged' or 'CustomerBrevo'.");

        RuleFor(x => x.SenderName)
            .NotEmpty().WithMessage("Sender name is required.")
            .MaximumLength(100);

        RuleFor(x => x.SenderEmail)
            .NotEmpty().WithMessage("Sender email is required in CustomerBrevo mode.")
            .EmailAddress().WithMessage("Sender email must be a valid email address.")
            .MaximumLength(256)
            .When(x => x.Mode == "CustomerBrevo");
    }
}
