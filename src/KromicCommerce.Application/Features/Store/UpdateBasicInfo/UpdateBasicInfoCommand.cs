namespace KromicCommerce.Application.Features.Store.UpdateBasicInfo;

public sealed record UpdateBasicInfoCommand(
    string BusinessName,
    string? LegalName,
    string? WebsiteUrl,
    string? SupportEmail,
    string? SupportPhone,
    string? Address,
    string? FacebookUrl,
    string? InstagramUrl,
    string? TwitterUrl,
    string? YoutubeUrl) : ICommand;
