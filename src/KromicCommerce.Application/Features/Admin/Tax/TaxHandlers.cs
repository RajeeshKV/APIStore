using KromicCommerce.Application.Caching;
using KromicCommerce.Contracts.Store;
using KromicCommerce.Domain.Store;
using Microsoft.Extensions.Caching.Memory;

namespace KromicCommerce.Application.Features.Admin.Tax;

// -----------------------------------------------------------------------
// Command + Query
// -----------------------------------------------------------------------

public sealed record UpdateTaxConfigCommand(
    bool TaxEnabled,
    decimal TaxPercentage,
    bool IsPriceInclusive,
    string? TaxLabel)
    : ICommand<TaxConfigResponse>;

public sealed record GetTaxConfigQuery() : IQuery<TaxConfigResponse>;

// -----------------------------------------------------------------------
// Validators
// -----------------------------------------------------------------------

internal sealed class UpdateTaxConfigValidator : AbstractValidator<UpdateTaxConfigCommand>
{
    public UpdateTaxConfigValidator()
    {
        RuleFor(x => x.TaxPercentage)
            .InclusiveBetween(0m, 100m)
            .WithMessage("Tax percentage must be between 0 and 100.");
        RuleFor(x => x.TaxPercentage)
            .GreaterThan(0m)
            .When(x => x.TaxEnabled)
            .WithMessage("Tax percentage must be > 0 when tax is enabled.");
        RuleFor(x => x.TaxLabel)
            .MaximumLength(50)
            .When(x => x.TaxLabel is not null);
    }
}

// -----------------------------------------------------------------------
// Get handler
// -----------------------------------------------------------------------

internal sealed class GetTaxConfigHandler(IBusinessSettingsService settingsService)
    : IQueryHandler<GetTaxConfigQuery, TaxConfigResponse>
{
    public async Task<Result<TaxConfigResponse>> Handle(
        GetTaxConfigQuery query, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct)
            ?? throw new InvalidOperationException("Business settings not found.");

        return Result.Success(TaxHandlerMapper.MapResponse(settings.Tax));
    }
}

// -----------------------------------------------------------------------
// Update handler
// -----------------------------------------------------------------------

internal sealed class UpdateTaxConfigHandler(
    IApplicationDbContext db,
    IBusinessSettingsService settingsService,
    IMemoryCache cache)
    : ICommandHandler<UpdateTaxConfigCommand, TaxConfigResponse>
{
    public async Task<Result<TaxConfigResponse>> Handle(
        UpdateTaxConfigCommand cmd, CancellationToken ct)
    {
        var settings = await db.BusinessSettings
            .FirstOrDefaultAsync(bs => bs.Id == BusinessSettings.SingletonId, ct);

        if (settings is null)
            return Result.Failure<TaxConfigResponse>(
                Error.NotFound("SETTINGS_NOT_FOUND", "Business settings not found."));

        TaxSettings taxSettings;
        try
        {
            taxSettings = TaxSettings.Create(
                cmd.TaxEnabled, cmd.TaxPercentage, cmd.IsPriceInclusive, cmd.TaxLabel);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<TaxConfigResponse>(
                Error.Validation("TAX_CONFIG_INVALID", ex.Message));
        }

        settings.UpdateTax(taxSettings);
        await db.SaveChangesAsync(ct);

        // Invalidate the cached business settings so the new tax config is picked up immediately
        settingsService.Invalidate();
        cache.Remove(CatalogCacheKeys.BusinessSettings);

        return Result.Success(TaxHandlerMapper.MapResponse(taxSettings));
    }
}

file static class TaxHandlerMapper
{
    public static TaxConfigResponse MapResponse(TaxSettings tax) =>
        new(tax.TaxEnabled, tax.TaxPercentage, tax.IsPriceInclusive, tax.TaxLabel);
}
