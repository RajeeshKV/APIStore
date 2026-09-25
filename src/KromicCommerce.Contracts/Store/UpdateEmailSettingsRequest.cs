namespace KromicCommerce.Contracts.Store;

public sealed record UpdateEmailSettingsRequest(
    string Mode,
    string SenderName,
    string? SenderEmail);
