namespace KromicCommerce.Application.Abstractions.Auth;

/// <summary>
/// Provides the identity of the currently authenticated user.
/// Populated from JWT claims — never from client-supplied headers or body.
/// </summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    string? Role { get; }
    bool IsAuthenticated { get; }
    int? TokenVersion { get; }
}
