using Asp.Versioning;
using KromicCommerce.Api.Extensions;
using KromicCommerce.Application.Abstractions.Auth;
using KromicCommerce.Application.Features.Auth.GoogleCallback;
using KromicCommerce.Application.Features.Auth.LoginWithEmail;
using KromicCommerce.Application.Features.Auth.Logout;
using KromicCommerce.Application.Features.Auth.LogoutAll;
using KromicCommerce.Application.Features.Auth.PasswordReset;
using KromicCommerce.Application.Features.Auth.RefreshToken;
using KromicCommerce.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace KromicCommerce.Api.Controllers.V1;

/// <summary>
/// Authentication endpoints.
///
/// Customer authentication: Google Sign-In only (POST /auth/google).
/// Admin authentication:    username or email + password (POST /auth/login).
///
/// There is no customer email/password registration or password-reset endpoint —
/// customers authenticate exclusively via Google OAuth.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(IMediator mediator, ICurrentUserService currentUser) : ControllerBase
{
    // -------------------------------------------------------------------------
    // Admin — username/email + password
    // -------------------------------------------------------------------------

    /// <summary>
    /// Admin login. Accepts either an email address or a username in the
    /// <c>identifier</c> field. Returns a JWT access token and a refresh token.
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitingExtensions.AuthPolicy)]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new LoginWithEmailCommand(request.Identifier, request.Password, request.DeviceHint),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : result.Error.ToActionResult();
    }

    // -------------------------------------------------------------------------
    // Customer — Google Sign-In
    // -------------------------------------------------------------------------

    /// <summary>
    /// Customer authentication via Google Sign-In.
    /// The client completes the Google Sign-In flow and POSTs the resulting
    /// Google ID token here. The backend validates the token with Google,
    /// finds or creates the customer account, and returns the application's
    /// own JWT access token and refresh token.
    /// The Google token is never used as an API authorization credential.
    /// </summary>
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

    // -------------------------------------------------------------------------
    // Shared — token lifecycle
    // -------------------------------------------------------------------------

    /// <summary>Refresh access token using a valid refresh token (rotation).</summary>
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

    /// <summary>Revoke the current device's refresh token (single device logout).</summary>
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

    /// <summary>
    /// Revoke all refresh tokens and increment token version (all devices logout).
    /// Existing short-lived access tokens remain valid until they expire naturally
    /// (up to <c>AccessTokenExpiryMinutes</c>).
    /// </summary>
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

    // -------------------------------------------------------------------------
    // Admin — password reset
    // -------------------------------------------------------------------------

    /// <summary>
    /// Request a password reset email for an admin account.
    /// Always returns 204 regardless of whether the email exists (prevents enumeration).
    /// Rate limited to 3 requests per 15 minutes per IP.
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
    /// Complete the admin password reset using the token received by email.
    /// On success all existing sessions are invalidated (token version incremented,
    /// all refresh tokens revoked).
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
