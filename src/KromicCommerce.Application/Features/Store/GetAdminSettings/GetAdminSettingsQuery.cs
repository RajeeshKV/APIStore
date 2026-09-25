namespace KromicCommerce.Application.Features.Store.GetAdminSettings;

/// <summary>Admin-only read — returns all configuration sections including auth and email.</summary>
public sealed record GetAdminSettingsQuery : IQuery<AdminBusinessSettingsResponse>;
