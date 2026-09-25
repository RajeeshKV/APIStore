using Asp.Versioning;
using KromicCommerce.Application.Features.Auth.BootstrapAdmin;
using KromicCommerce.Contracts.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// One-time admin bootstrap. Rejected if an admin already exists.
/// Requires a bootstrap secret from the App:BootstrapSecret env var.
/// This endpoint is intentionally unauthenticated — there are no users yet.
/// The bootstrap secret acts as the credential.
/// Rate-limited aggressively to prevent brute-force on the bootstrap secret.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/bootstrap")]
public sealed class AdminBootstrapController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitingExtensions.PasswordResetPolicy)] // 3 requests per 15 min per IP
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Bootstrap(
        [FromBody] BootstrapAdminRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new BootstrapAdminCommand(
                request.Email, request.Password,
                request.FirstName, request.LastName,
                request.BusinessName, request.BootstrapSecret,
                request.Username),
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : result.Error.ToActionResult();
    }
}
