namespace KromicCommerce.Contracts.Store;

public sealed record UpdateTaxConfigRequest(
    bool TaxEnabled,
    decimal TaxPercentage,
    bool IsPriceInclusive,
    string? TaxLabel);

public sealed record TaxConfigResponse(
    bool TaxEnabled,
    decimal TaxPercentage,
    bool IsPriceInclusive,
    string TaxLabel);
