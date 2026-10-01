using Asp.Versioning;
using KromicCommerce.Api.Extensions;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Auth.SendOtp;
using KromicCommerce.Application.Features.Auth.VerifyOtp;
using KromicCommerce.Application.Features.Auth.PhoneVerificationStatus;
using KromicCommerce.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace KromicCommerce.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/otp")]
public sealed class OtpController(IMediator mediator, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>Send an OTP to the specified phone number.</summary>
    [HttpPost("send")]
    [EnableRateLimiting(RateLimitingExtensions.OtpPolicy)]
    [ProducesResponseType(typeof(OtpSendResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Send(
        [FromBody] OtpSendRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new SendOtpCommand(request.PhoneNumber, request.Purpose, currentUser.UserId),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.Error.ToActionResult();
    }

    /// <summary>Verify an OTP.</summary>
    [HttpPost("verify")]
    [EnableRateLimiting(RateLimitingExtensions.OtpPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Verify(
        [FromBody] OtpVerifyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new VerifyOtpCommand(request.PhoneNumber, request.Otp, request.Purpose, currentUser.UserId),
            cancellationToken);

        return result.IsSuccess
            ? NoContent()
            : result.Error.ToActionResult();
    }

    /// <summary>
    /// Whether the signed-in customer must verify a phone number before checkout, and whether
    /// they already have. Clients should gate on <c>verificationSatisfied</c> rather than
    /// inferring the rule from their own copy of the settings.
    /// </summary>
    [HttpGet("verification-status")]
    [Authorize]
    [ProducesResponseType(typeof(PhoneVerificationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerificationStatus(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Unauthorized();

        var result = await mediator.Send(
            new GetPhoneVerificationStatusQuery(userId), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.Error.ToActionResult();
    }
}
