using System.Security.Claims;
using KromicCommerce.Application.Abstractions.Auth;
using Microsoft.AspNetCore.Http;

namespace KromicCommerce.Infrastructure.Auth;

internal sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? Principal =>
        httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var sub = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? Principal?.FindFirstValue("sub");
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email)
                         ?? Principal?.FindFirstValue("email");

    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role)
                        ?? Principal?.FindFirstValue("role");

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated is true;

    public int? TokenVersion
    {
        get
        {
            var tv = Principal?.FindFirstValue("tv");
            return int.TryParse(tv, out var v) ? v : null;
        }
    }
}
