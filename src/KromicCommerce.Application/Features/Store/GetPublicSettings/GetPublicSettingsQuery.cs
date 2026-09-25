namespace KromicCommerce.Application.Features.Store.GetPublicSettings;

/// <summary>Unauthenticated read — returns only public-safe fields.</summary>
public sealed record GetPublicSettingsQuery : IQuery<PublicBusinessSettingsResponse>;
