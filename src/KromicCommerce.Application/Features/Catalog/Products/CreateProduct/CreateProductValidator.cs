namespace KromicCommerce.Application.Features.Catalog.Products.CreateProduct;

internal sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Slug)
            .NotEmpty().MaximumLength(500)
            .Matches(@"^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .WithMessage("Slug must be lowercase alphanumeric with hyphens only.");
        RuleFor(x => x.Sku).MaximumLength(100).When(x => x.Sku is not null);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).WithMessage("Price must be >= 0.");
        RuleFor(x => x.CompareAtPrice)
            .GreaterThan(x => x.Price)
            .WithMessage("Compare-at price must be greater than the selling price.")
            .When(x => x.CompareAtPrice.HasValue);
        RuleFor(x => x.Description).MaximumLength(10000).When(x => x.Description is not null);
        RuleFor(x => x.ShortDescription).MaximumLength(500).When(x => x.ShortDescription is not null);
        RuleFor(x => x.MetaTitle).MaximumLength(120).When(x => x.MetaTitle is not null);
        RuleFor(x => x.MetaDescription).MaximumLength(320).When(x => x.MetaDescription is not null);
        RuleFor(x => x.MetaKeywords).MaximumLength(500).When(x => x.MetaKeywords is not null);
    }
}
