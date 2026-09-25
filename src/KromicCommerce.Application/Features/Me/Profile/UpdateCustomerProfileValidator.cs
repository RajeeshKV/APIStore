namespace KromicCommerce.Application.Features.Me.Profile;

internal sealed class UpdateCustomerProfileValidator : AbstractValidator<UpdateCustomerProfileCommand>
{
    public UpdateCustomerProfileValidator()
    {
        RuleFor(x => x.DisplayName).MaximumLength(100).When(x => x.DisplayName is not null);
        RuleFor(x => x.PhoneNumber)
            .Matches(@"^\+?[1-9]\d{6,14}$").WithMessage("Phone number must be in E.164 format.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhoneNumber));
        RuleFor(x => x.PreferredTimeZoneId)
            .MaximumLength(100)
            .When(x => x.PreferredTimeZoneId is not null);
        RuleFor(x => x.DateOfBirth)
            .LessThan(DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth must be in the past.")
            .When(x => x.DateOfBirth.HasValue);
    }
}
