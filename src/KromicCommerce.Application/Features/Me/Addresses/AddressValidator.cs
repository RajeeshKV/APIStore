namespace KromicCommerce.Application.Features.Me.Addresses;

internal sealed class CreateCustomerAddressValidator : AbstractValidator<CreateCustomerAddressCommand>
{
    public CreateCustomerAddressValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Company).MaximumLength(200).When(x => x.Company is not null);
        RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(300);
        RuleFor(x => x.AddressLine2).MaximumLength(300).When(x => x.AddressLine2 is not null);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.CountryCode)
            .NotEmpty().Length(2).WithMessage("Country code must be exactly 2 characters (ISO 3166-1).");
        RuleFor(x => x.Phone)
            .Matches(@"^\+?[1-9]\d{6,14}$").WithMessage("Phone must be in E.164 format.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.Label).MaximumLength(50).When(x => x.Label is not null);
    }
}

internal sealed class UpdateCustomerAddressValidator : AbstractValidator<UpdateCustomerAddressCommand>
{
    public UpdateCustomerAddressValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Company).MaximumLength(200).When(x => x.Company is not null);
        RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(300);
        RuleFor(x => x.AddressLine2).MaximumLength(300).When(x => x.AddressLine2 is not null);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.State).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.CountryCode)
            .NotEmpty().Length(2).WithMessage("Country code must be exactly 2 characters.");
        RuleFor(x => x.Phone)
            .Matches(@"^\+?[1-9]\d{6,14}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
        RuleFor(x => x.Label).MaximumLength(50).When(x => x.Label is not null);
    }
}
