using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using KromicCommerce.Application.Options;

namespace KromicCommerce.Application.Features.Auth.SendOtp;

internal sealed class SendOtpHandler(
    IApplicationDbContext db,
    IOtpService otpService,
    ISmsProviderFactory smsProviderFactory,
    IOptions<SmsPolicyOptions> smsPolicyOptions,
    ILogger<SendOtpHandler> logger)
    : ICommandHandler<SendOtpCommand, OtpSendResponse>
{
    public async Task<Result<OtpSendResponse>> Handle(
        SendOtpCommand command,
        CancellationToken cancellationToken)
    {
        var policy = smsPolicyOptions.Value;
        var provider = smsProviderFactory.Create();

        // Refuse before doing any work when nothing can be delivered. Returning a success
        // shape here would tell the customer a code is on its way when none was sent.
        if (!provider.IsOperational)
        {
            logger.LogError("OTP requested but no SMS provider is configured. Purpose: {Purpose}", command.Purpose);
            return Result.Failure<OtpSendResponse>(
                Error.Failure("SMS_NOT_CONFIGURED",
                    "Verification codes are unavailable right now. Please contact support."));
        }

        if (SmsPhoneNumber.TryToE164(command.PhoneNumber) is not { } canonicalPhone)
        {
            return Result.Failure<OtpSendResponse>(
                Error.Validation("INVALID_PHONE_NUMBER", "Enter a valid 10-digit mobile number."));
        }

        // Cooldown: reject if a recent unexpired OTP was sent for the same phone and purpose.
        // An earlier code is invalidated by superseding it, so the customer is never left
        // guessing which of two live codes to type.
        var now = DateTime.UtcNow;
        var recentCutoff = now.AddSeconds(-policy.ResendCooldownSeconds);
        var previous = await db.OtpRequests
            .Where(o => o.PhoneNumber == canonicalPhone
                     && o.Purpose == command.Purpose
                     && o.CreatedAt > recentCutoff
                     && o.VerifiedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (previous is not null)
        {
            var resendAt = previous.CreatedAt.AddSeconds(policy.ResendCooldownSeconds);
            return Result.Failure<OtpSendResponse>(
                Error.Conflict("OTP_COOLDOWN",
                    $"Please wait before requesting another code. Resend available after {resendAt:u}."));
        }

        var rawOtp = otpService.GenerateOtp(policy.Length);

        // Deliver first. Persisting before the send would leave an unverifiable code in the
        // table on failure, consuming a cooldown window the customer never used.
        var result = await provider.SendOtpAsync(canonicalPhone, rawOtp, cancellationToken);

        if (!result.Success)
        {
            // The raw OTP is never logged — only the provider's own error code.
            logger.LogWarning(
                "SMS provider {Provider} failed to send OTP. ErrorCode: {Code}, Retryable: {Retryable}",
                provider.ProviderName, result.ErrorCode, result.Retryable);

            return Result.Failure<OtpSendResponse>(
                Error.Failure("OTP_SEND_FAILED", "Could not send the verification code. Please try again."));
        }

        // Only the hash is ever persisted.
        var expiresAt = now.AddMinutes(policy.ExpiryMinutes);
        var otpRequest = OtpRequest.Create(
            canonicalPhone,
            otpService.HashOtp(rawOtp),
            command.Purpose,
            expiresAt,
            command.UserId,
            policy.MaxAttempts);

        db.OtpRequests.Add(otpRequest);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "OTP sent for {Purpose} via {Provider} to phone ending ...{Suffix}",
            command.Purpose, provider.ProviderName, SmsPhoneNumber.Mask(canonicalPhone));

        return Result.Success(new OtpSendResponse(
            ExpiresAtUtc: expiresAt,
            ResendAvailableAtUtc: now.AddSeconds(policy.ResendCooldownSeconds)));
    }
}
