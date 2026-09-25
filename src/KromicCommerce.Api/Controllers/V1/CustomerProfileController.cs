using Asp.Versioning;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Me.Profile;
using KromicCommerce.Contracts.Me;
using Microsoft.AspNetCore.Authorization;

namespace KromicCommerce.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/customer/profile")]
[Authorize]
public sealed class CustomerProfileController(
    IMediator mediator, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(CustomerProfileResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId is null) return Unauthorized();
        var result = await mediator.Send(new GetCustomerProfileQuery(userId.Value), ct);
        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }

    [HttpPut]
    [ProducesResponseType(typeof(CustomerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateCustomerProfileRequest request, CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId is null) return Unauthorized();

        var result = await mediator.Send(new UpdateCustomerProfileCommand(
            userId.Value,
            request.DisplayName, request.DateOfBirth,
            request.PhoneNumber, request.NewsletterConsent,
            request.PreferredTimeZoneId), ct);

        return result.IsSuccess ? Ok(result.Value) : result.Error.ToActionResult();
    }
}
