using Asp.Versioning;
using KromicCommerce.Api.Extensions;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Auth.SendOtp;
using KromicCommerce.Application.Features.Auth.VerifyOtp;
using KromicCommerce.Contracts.Auth;
using KromicCommerce.Domain.Identity;
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
        if (!Enum.TryParse<OtpPurpose>(request.Purpose, ignoreCase: true, out var purpose))
            return BadRequest(new { error = new { code = "INVALID_PURPOSE", message = "Invalid OTP purpose." } });

        var result = await mediator.Send(
            new SendOtpCommand(request.PhoneNumber, purpose, currentUser.UserId),
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
        if (!Enum.TryParse<OtpPurpose>(request.Purpose, ignoreCase: true, out var purpose))
            return BadRequest(new { error = new { code = "INVALID_PURPOSE", message = "Invalid OTP purpose." } });

        var result = await mediator.Send(
            new VerifyOtpCommand(request.PhoneNumber, request.Otp, purpose, currentUser.UserId),
            cancellationToken);

        return result.IsSuccess
            ? NoContent()
            : result.Error.ToActionResult();
    }
}
