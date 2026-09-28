using KromicCommerce.Domain.Store;

namespace KromicCommerce.Application.Features.Store.UpdateEmailSettings;

public sealed record UpdateEmailSettingsCommand(
    EmailMode Mode,
    string SenderName,
    string? SenderEmail) : ICommand;
