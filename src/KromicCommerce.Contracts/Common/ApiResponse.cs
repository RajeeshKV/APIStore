namespace KromicCommerce.Contracts.Common;

/// <summary>
/// Consistent API response envelope matching the error format in docs/16-API-Standards.md.
/// Success responses may use T directly for 200/201 (envelope optional).
/// Error responses always use this shape.
/// </summary>
public sealed record ApiErrorResponse(
    bool Success,
    ApiErrorDetail Error,
    string? TraceId = null);

public sealed record ApiErrorDetail(
    string Code,
    string Message);
