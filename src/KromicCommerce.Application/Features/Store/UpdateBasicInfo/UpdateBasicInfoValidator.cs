namespace KromicCommerce.Application.Features.Store.UpdateBasicInfo;

internal sealed class UpdateBasicInfoValidator : AbstractValidator<UpdateBasicInfoCommand>
{
    public UpdateBasicInfoValidator()
    {
        RuleFor(x => x.BusinessName)
            .NotEmpty().WithMessage("Business name is required.")
            .MaximumLength(200);

        RuleFor(x => x.LegalName).MaximumLength(200).When(x => x.LegalName is not null);
        RuleFor(x => x.WebsiteUrl).MaximumLength(512).When(x => x.WebsiteUrl is not null);
        RuleFor(x => x.Address).MaximumLength(500).When(x => x.Address is not null);

        RuleFor(x => x.SupportEmail)
            .EmailAddress().WithMessage("Support email must be a valid email address.")
            .MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.SupportEmail));

        RuleFor(x => x.SupportPhone).MaximumLength(20).When(x => x.SupportPhone is not null);

        RuleFor(x => x.FacebookUrl).MaximumLength(512).When(x => x.FacebookUrl is not null);
        RuleFor(x => x.InstagramUrl).MaximumLength(512).When(x => x.InstagramUrl is not null);
        RuleFor(x => x.TwitterUrl).MaximumLength(512).When(x => x.TwitterUrl is not null);
        RuleFor(x => x.YoutubeUrl).MaximumLength(512).When(x => x.YoutubeUrl is not null);
    }
}
