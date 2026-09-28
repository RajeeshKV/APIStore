using KromicCommerce.Domain.Store;

namespace KromicCommerce.Application.Features.Store.UpdateEmailSettings;

internal sealed class UpdateEmailSettingsValidator : AbstractValidator<UpdateEmailSettingsCommand>
{
    public UpdateEmailSettingsValidator()
    {
        RuleFor(x => x.Mode)
            .IsInEnum()
            .WithMessage("Mode must be a valid EmailMode value.");

        RuleFor(x => x.SenderName)
            .NotEmpty().WithMessage("Sender name is required.")
            .MaximumLength(100);

        RuleFor(x => x.SenderEmail)
            .NotEmpty().WithMessage("Sender email is required in CustomerBrevo mode.")
            .EmailAddress().WithMessage("Sender email must be a valid email address.")
            .MaximumLength(256)
            .When(x => x.Mode == EmailMode.CustomerBrevo);
    }
}
