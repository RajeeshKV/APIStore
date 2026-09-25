using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Features.Auth.VerifyOtp;

internal sealed class VerifyOtpHandler(
    IApplicationDbContext db,
    IOtpService otpService,
    ILogger<VerifyOtpHandler> logger)
    : ICommandHandler<VerifyOtpCommand>
{
    private static readonly Error InvalidOtp =
        Error.Validation("OTP_INVALID", "The OTP is invalid or expired.");

    public async Task<Result> Handle(VerifyOtpCommand command, CancellationToken cancellationToken)
    {
        var otpRequest = await db.OtpRequests
            .Where(o => o.PhoneNumber == command.PhoneNumber
                     && o.Purpose == command.Purpose
                     && o.VerifiedAt == null)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (otpRequest is null || !otpRequest.CanAttempt())
            return Result.Failure(InvalidOtp);

        var exhausted = otpRequest.RecordAttempt();

        if (!otpService.VerifyOtp(command.SubmittedOtp, otpRequest.OtpHash))
        {
            await db.SaveChangesAsync(cancellationToken);

            if (exhausted)
            {
                logger.LogWarning(
                    "OTP max attempts reached for phone ending ...{Suffix}",
                    command.PhoneNumber.Length > 4 ? command.PhoneNumber[^4..] : "****");
                return Result.Failure(Error.Validation("OTP_MAX_ATTEMPTS", "Maximum verification attempts exceeded."));
            }

            return Result.Failure(InvalidOtp);
        }

        otpRequest.MarkVerified();

        // Mark user phone verified if applicable
        if (command.UserId.HasValue && command.Purpose == OtpPurpose.PhoneVerification)
        {
            var user = await db.Users.FindAsync([command.UserId.Value], cancellationToken);
            user?.SetPhoneNumber(command.PhoneNumber, verified: true);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("OTP verified for {Purpose}", command.Purpose);

        return Result.Success();
    }
}
