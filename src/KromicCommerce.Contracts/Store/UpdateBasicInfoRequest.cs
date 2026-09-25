namespace KromicCommerce.Contracts.Store;

public sealed record UpdateBasicInfoRequest(
    string BusinessName,
    string? LegalName,
    string? WebsiteUrl,
    string? SupportEmail,
    string? SupportPhone,
    string? Address,
    string? FacebookUrl,
    string? InstagramUrl,
    string? TwitterUrl,
    string? YoutubeUrl,
    string? WhatsAppNumber = null,
    string? LinkedInUrl = null);
