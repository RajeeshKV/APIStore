namespace KromicCommerce.Application.Features.Catalog.Inventory;

internal sealed class SetStockValidator : AbstractValidator<SetStockCommand>
{
    public SetStockValidator()
    {
        RuleFor(x => x.OnHand).GreaterThanOrEqualTo(0).WithMessage("On-hand stock cannot be negative.");
        RuleFor(x => x.LowStockThreshold).GreaterThanOrEqualTo(0);
    }
}

internal sealed class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockValidator()
    {
        RuleFor(x => x.Delta).NotEqual(0).WithMessage("Stock delta must not be zero.");
        RuleFor(x => x.Reason).MaximumLength(500).When(x => x.Reason is not null);
    }
}
