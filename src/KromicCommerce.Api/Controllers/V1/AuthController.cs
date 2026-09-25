using Asp.Versioning;
using KromicCommerce.Api.Extensions;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Auth.GoogleCallback;
using KromicCommerce.Application.Features.Auth.LoginWithEmail;
using KromicCommerce.Application.Features.Auth.Logout;
using KromicCommerce.Application.Features.Auth.LogoutAll;
using KromicCommerce.Application.Features.Auth.PasswordReset;
using KromicCommerce.Application.Features.Auth.RefreshToken;
using KromicCommerce.Application.Features.Auth.RegisterCustomer;
using KromicCommerce.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace KromicCommerce.Api.Controllers.V1;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(IMediator mediator, ICurrentUserService currentUser) : ControllerBase
{
    /// <summary>Register a new customer account.</summary>
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new RegisterCustomerCommand(request.Email, request.Password,
                request.FirstName, request.LastName, request.PhoneNumber, null),
            cancellationToken);

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : result.Error.ToActionResult();
    }

    /// <summary>Login with email and password.</summary>
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new LoginWithEmailCommand(request.Email, request.Password, request.DeviceHint),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.Error.ToActionResult();
    }

    /// <summary>Authenticate via Google Sign-In ID token.</summary>
    [HttpPost("google")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GoogleCallback(
        [FromBody] GoogleCallbackRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GoogleCallbackCommand(request.IdToken, request.DeviceHint),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.Error.ToActionResult();
    }

    /// <summary>Refresh access token using a valid refresh token.</summary>
    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new RefreshTokenCommand(request.RefreshToken, request.DeviceHint),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.Error.ToActionResult();
    }

    /// <summary>Revoke the current refresh token (single device logout).</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null) return Unauthorized();

        await mediator.Send(new LogoutCommand(request.RefreshToken, userId.Value), cancellationToken);
        return NoContent();
    }

    /// <summary>Revoke all refresh tokens and increment token version (all devices logout).</summary>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null) return Unauthorized();

        await mediator.Send(new LogoutAllCommand(userId.Value), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Request a password reset email for an admin account.
    /// Always returns 204 regardless of whether the email exists (prevents enumeration).
    /// </summary>
    [HttpPost("request-password-reset")]
    [EnableRateLimiting(RateLimitingExtensions.PasswordResetPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestPasswordReset(
        [FromBody] RequestPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new RequestPasswordResetCommand(request.Email),
            cancellationToken);
        // Always return 204 — never reveal whether the email exists
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }

    /// <summary>
    /// Complete the password reset using the token received by email.
    /// On success all existing sessions are invalidated.
    /// </summary>
    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitingExtensions.PasswordResetPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (request.NewPassword != request.ConfirmPassword)
            return BadRequest(new { error = "Passwords do not match." });

        var result = await mediator.Send(
            new ResetPasswordCommand(request.Email, request.Token, request.NewPassword),
            cancellationToken);
        return result.IsSuccess ? NoContent() : result.Error.ToActionResult();
    }
}
