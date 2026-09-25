namespace KromicCommerce.Application.Features.Catalog.Products.Variants;

internal sealed class CreateVariantValidator : AbstractValidator<CreateVariantCommand>
{
    public CreateVariantValidator()
    {
        RuleFor(x => x.Sku).MaximumLength(100).When(x => x.Sku is not null);
        RuleFor(x => x.PriceOverride)
            .GreaterThanOrEqualTo(0).WithMessage("Variant price override must be >= 0.")
            .When(x => x.PriceOverride.HasValue);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

internal sealed class UpdateVariantValidator : AbstractValidator<UpdateVariantCommand>
{
    public UpdateVariantValidator()
    {
        RuleFor(x => x.Sku).MaximumLength(100).When(x => x.Sku is not null);
        RuleFor(x => x.PriceOverride)
            .GreaterThanOrEqualTo(0).WithMessage("Variant price override must be >= 0.")
            .When(x => x.PriceOverride.HasValue);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}
