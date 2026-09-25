namespace KromicCommerce.Application.Features.Store.UpdateLocale;

internal sealed class UpdateLocaleValidator : AbstractValidator<UpdateLocaleCommand>
{
    public UpdateLocaleValidator()
    {
        RuleFor(x => x.CountryCode)
            .NotEmpty()
            .Length(2).WithMessage("CountryCode must be exactly 2 characters (ISO 3166-1 alpha-2).")
            .Matches(@"^[A-Za-z]{2}$");

        RuleFor(x => x.CurrencyCode)
            .NotEmpty()
            .Length(3).WithMessage("CurrencyCode must be exactly 3 characters (ISO 4217).")
            .Matches(@"^[A-Za-z]{3}$");

        RuleFor(x => x.TimeZoneId)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Culture)
            .NotEmpty()
            .MaximumLength(20)
            .Matches(@"^[a-zA-Z]{2}-[a-zA-Z]{2}$")
            .WithMessage("Culture must be in format 'xx-XX' (e.g. en-IN).");
    }
}
