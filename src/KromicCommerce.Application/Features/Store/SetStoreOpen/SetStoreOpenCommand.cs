namespace KromicCommerce.Application.Features.Store.SetStoreOpen;

public sealed record SetStoreOpenCommand(
    bool IsOpen,
    string? ClosureMessage) : ICommand;
