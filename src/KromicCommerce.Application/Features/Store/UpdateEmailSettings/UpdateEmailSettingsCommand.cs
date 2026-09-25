namespace KromicCommerce.Application.Features.Store.UpdateEmailSettings;

public sealed record UpdateEmailSettingsCommand(
    string Mode,
    string SenderName,
    string? SenderEmail) : ICommand;
