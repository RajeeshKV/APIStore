namespace KromicCommerce.Contracts.Admin;

/// <summary>
/// A gateway the administrator may select.
/// </summary>
/// <param name="Name">Canonical provider name, as written to configuration.</param>
/// <param name="Description">What the gateway requires, so the admin screen can explain prerequisites.</param>
public sealed record SmsProviderOptionResponse(string Name, string Description);

/// <summary>
/// An SMS message template as returned on the admin surface.
/// </summary>
/// <param name="Id">Template identifier.</param>
/// <param name="Provider">The gateway this template is registered with.</param>
/// <param name="Name">Admin-facing label.</param>
/// <param name="Body">The message body containing placeholder tokens, or empty.</param>
/// <param name="ExternalTemplateId">Vendor-side template registration identifier, or null.</param>
/// <param name="IsActive">True when this template is the one used for OTP delivery.</param>
/// <param name="UpdatedAtUtc">Last modification time.</param>
public sealed record SmsTemplateResponse(
    Guid Id,
    string Provider,
    string Name,
    string Body,
    string? ExternalTemplateId,
    bool IsActive,
    DateTime UpdatedAtUtc);

/// <summary>Creates a template. The provider cannot be changed afterwards.</summary>
public sealed record CreateSmsTemplateRequest(
    string Provider,
    string Name,
    string? Body,
    string? ExternalTemplateId,
    bool IsActive = true);

/// <summary>Edits a template. <see cref="IsActive"/> promotes or demotes it for the provider.</summary>
public sealed record UpdateSmsTemplateRequest(
    string Name,
    string? Body,
    string? ExternalTemplateId,
    bool IsActive);
