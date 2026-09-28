using KromicCommerce.Domain.Store;

namespace KromicCommerce.Contracts.Store;

public sealed record EmailSettingsDto(
    EmailMode Mode,
    string SenderName,
    string? SenderEmail);
