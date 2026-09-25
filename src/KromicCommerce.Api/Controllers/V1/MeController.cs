using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Auth.GetCurrentUser;
using KromicCommerce.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/me")]
[Authorize]
public sealed class MeController(IMediator mediator, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>Get the currently authenticated user's profile.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null) return Unauthorized();

        var result = await mediator.Send(new GetCurrentUserQuery(userId.Value), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.Error.ToActionResult();
    }
}
