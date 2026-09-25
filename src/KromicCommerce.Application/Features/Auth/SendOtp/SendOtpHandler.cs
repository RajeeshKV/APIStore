using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Features.Auth.SendOtp;

internal sealed class SendOtpHandler(
    IApplicationDbContext db,
    IOtpService otpService,
    ISmsProvider smsProvider,
    ILogger<SendOtpHandler> logger)
    : ICommandHandler<SendOtpCommand, OtpSendResponse>
{
    private const int OtpExpiryMinutes = 10;
    private const int ResendCooldownSeconds = 60;

    public async Task<Result<OtpSendResponse>> Handle(
        SendOtpCommand command,
        CancellationToken cancellationToken)
    {
        // Cooldown: reject if a recent unexpired OTP was sent for same phone+purpose
        var recentCutoff = DateTime.UtcNow.AddSeconds(-ResendCooldownSeconds);
        var recent = await db.OtpRequests
            .Where(o => o.PhoneNumber == command.PhoneNumber
                     && o.Purpose == command.Purpose
                     && o.CreatedAt > recentCutoff
                     && o.VerifiedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (recent is not null)
        {
            var resendAt = recent.CreatedAt.AddSeconds(ResendCooldownSeconds);
            return Result.Failure<OtpSendResponse>(
                Error.Conflict("OTP_COOLDOWN",
                    $"Please wait before requesting another OTP. Resend available after {resendAt:u}."));
        }

        var rawOtp = otpService.GenerateOtp();
        // IMPORTANT: never log rawOtp
        var otpHash = otpService.HashOtp(rawOtp);
        var expiresAt = DateTime.UtcNow.AddMinutes(OtpExpiryMinutes);

        var otpRequest = OtpRequest.Create(command.PhoneNumber, otpHash, command.Purpose, expiresAt, command.UserId);
        db.OtpRequests.Add(otpRequest);
        await db.SaveChangesAsync(cancellationToken);

        // Send via SMS provider — failure is logged but does not throw
        var result = await smsProvider.SendOtpAsync(command.PhoneNumber, rawOtp, cancellationToken);

        if (!result.Success)
        {
            logger.LogWarning(
                "SMS provider {Provider} failed to send OTP. ErrorCode: {Code}, Retryable: {Retryable}",
                smsProvider.ProviderName, result.ErrorCode, result.Retryable);

            return Result.Failure<OtpSendResponse>(
                Error.Failure("OTP_SEND_FAILED", "Failed to send OTP. Please try again."));
        }

        logger.LogInformation(
            "OTP sent for {Purpose} to phone ending ...{Suffix}",
            command.Purpose,
            command.PhoneNumber.Length > 4 ? command.PhoneNumber[^4..] : "****");

        return Result.Success(new OtpSendResponse(
            ExpiresAtUtc: expiresAt,
            ResendAvailableAtUtc: otpRequest.CreatedAt.AddSeconds(ResendCooldownSeconds)));
    }
}
