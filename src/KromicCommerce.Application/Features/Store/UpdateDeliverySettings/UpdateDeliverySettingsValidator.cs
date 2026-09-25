namespace KromicCommerce.Application.Features.Store.UpdateDeliverySettings;

internal sealed class UpdateDeliverySettingsValidator : AbstractValidator<UpdateDeliverySettingsCommand>
{
    public UpdateDeliverySettingsValidator()
    {
        RuleFor(x => x.FlatFeeAmount)
            .GreaterThanOrEqualTo(0).WithMessage("Flat fee must be >= 0.");

        RuleFor(x => x.FreeShippingThreshold)
            .GreaterThan(0).WithMessage("Free-shipping threshold must be > 0.")
            .When(x => x.FreeShippingThreshold.HasValue);

        RuleFor(x => x.CodExtraFee)
            .GreaterThanOrEqualTo(0).WithMessage("COD extra fee must be >= 0.");

        RuleFor(x => x.ProcessingDays)
            .GreaterThanOrEqualTo(0).WithMessage("Processing days must be >= 0.");

        RuleFor(x => x.MinDeliveryDays)
            .GreaterThanOrEqualTo(0).WithMessage("Min delivery days must be >= 0.");

        RuleFor(x => x.MaxDeliveryDays)
            .GreaterThanOrEqualTo(x => x.MinDeliveryDays)
            .WithMessage("Max delivery days must be >= min delivery days.");
    }
}
