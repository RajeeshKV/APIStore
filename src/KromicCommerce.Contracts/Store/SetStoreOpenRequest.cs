namespace KromicCommerce.Contracts.Store;

public sealed record SetStoreOpenRequest(
    bool IsOpen,
    string? ClosureMessage = null);
