namespace KromicCommerce.Contracts.Store;

public sealed record EmailSettingsDto(
    string Mode,        // "KromicManaged" | "CustomerBrevo"
    string SenderName,
    string? SenderEmail);
