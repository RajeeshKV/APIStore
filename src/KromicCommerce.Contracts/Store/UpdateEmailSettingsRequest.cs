using KromicCommerce.Domain.Store;

namespace KromicCommerce.Contracts.Store;

public sealed record UpdateEmailSettingsRequest(
    EmailMode Mode,
    string SenderName,
    string? SenderEmail);
