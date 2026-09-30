using KromicCommerce.Domain.Store;

namespace KromicCommerce.Application.Features.Store.UpdateEmailSettings;

/// <summary>
/// Validation for the email settings update.
///
/// The two modes require different things, and that difference is the whole point of the
/// endpoint:
///
///   KromicManaged  — emails are sent from Kromic's shared account. Provider credentials do
///                    not exist for the store to configure, and the sender address is
///                    irrelevant, so it is neither required nor accepted. An admin UI should
///                    hide and disable its sender-address field in this mode.
///   CustomerBrevo  — the store sends from its own Brevo account, so a verified sender
///                    address is mandatory and must be a valid email address.
///
/// SenderName is always required: it labels the "From" line in both modes.
///
/// Validation mirrors the domain factory exactly. Previously the validator's NotEmpty()
/// accepted whitespace-only input while the domain used IsNullOrWhiteSpace, so a
/// whitespace address produced an unhandled ArgumentException (HTTP 500) instead of a
/// validation error. That class of mismatch is what caused the 500; both layers now agree,
/// and the validator runs first so the domain guard is only defence in depth.
/// </summary>
internal sealed class UpdateEmailSettingsValidator : AbstractValidator<UpdateEmailSettingsCommand>
{
    public UpdateEmailSettingsValidator()
    {
        RuleFor(x => x.Mode)
            .IsInEnum()
            .WithMessage("Mode must be a valid EmailMode value.");

        RuleFor(x => x.SenderName)
            .NotEmpty().WithMessage("Sender name is required.")
            .MaximumLength(100).WithMessage("Sender name must not exceed 100 characters.");

        // Sender address is required, and must be a real address, only in CustomerBrevo mode.
        RuleFor(x => x.SenderEmail)
            .NotEmpty().WithMessage("Sender email is required in CustomerBrevo mode.")
            .Must(NotBeWhitespace).WithMessage("Sender email is required in CustomerBrevo mode.")
            .EmailAddress().WithMessage("Sender email must be a valid email address.")
            .Must(HaveDottedDomain).WithMessage("Sender email must include a domain, e.g. store@example.com.")
            .MaximumLength(256).WithMessage("Sender email must not exceed 256 characters.")
            .When(x => x.Mode == EmailMode.CustomerBrevo);

        // In KromicManaged mode the address is not used at all, so a supplied value must not
        // be silently stored. Rejecting it tells the admin UI to stop sending the field
        // instead of appearing to succeed and having the value discarded server-side.
        RuleFor(x => x.SenderEmail)
            .Must(BeAbsent).WithMessage(
                "Sender email is not used in KromicManaged mode. Omit it, or switch to CustomerBrevo.")
            .When(x => x.Mode == EmailMode.KromicManaged && !string.IsNullOrWhiteSpace(x.SenderEmail));
    }

    private static bool NotBeWhitespace(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool BeAbsent(string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// FluentValidation's EmailAddress accepts hostnames with no dotted domain, so
    /// "store@example" passes as a valid address. Brevo rejects such a sender when the store
    /// tries to send, which surfaces as a runtime delivery failure rather than a validation
    /// error. Requiring a dotted domain rejects it up front, where the admin can fix it.
    /// </summary>
    private static bool HaveDottedDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;   // emptiness is reported by another rule

        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at < 0) return false;                            // no local part separator

        var domain = value[(at + 1)..];
        return domain.Contains('.') &&
               !domain.StartsWith('.') &&
               !domain.EndsWith('.');
    }
}
