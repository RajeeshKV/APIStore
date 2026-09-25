namespace KromicCommerce.Application.Features.Store.UpdateSeoSettings;

internal sealed class UpdateSeoSettingsValidator : AbstractValidator<UpdateSeoSettingsCommand>
{
    public UpdateSeoSettingsValidator()
    {
        RuleFor(x => x.MetaTitle).MaximumLength(120).When(x => x.MetaTitle is not null);
        RuleFor(x => x.MetaDescription).MaximumLength(320).When(x => x.MetaDescription is not null);
        RuleFor(x => x.MetaKeywords).MaximumLength(500).When(x => x.MetaKeywords is not null);
        RuleFor(x => x.FaviconUrl).MaximumLength(1024).When(x => x.FaviconUrl is not null);
        RuleFor(x => x.OgImageUrl).MaximumLength(1024).When(x => x.OgImageUrl is not null);
    }
}
