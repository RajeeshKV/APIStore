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
        // Match on the canonical form, otherwise a code sent to "+91 98765 43210" can never be
        // verified by a client that echoes back "9876543210".
        if (SmsPhoneNumber.TryToE164(command.PhoneNumber) is not { } canonicalPhone)
            return Result.Failure(Error.Validation("INVALID_PHONE_NUMBER",
                "Enter a valid 10-digit mobile number."));

        var otpRequest = await db.OtpRequests
            .Where(o => o.PhoneNumber == canonicalPhone
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
                    SmsPhoneNumber.Mask(canonicalPhone));
                return Result.Failure(Error.Validation("OTP_MAX_ATTEMPTS", "Maximum verification attempts exceeded."));
            }

            return Result.Failure(InvalidOtp);
        }

        otpRequest.MarkVerified();

        // A verified code is the single thing that promotes a phone to "verified", so the
        // user update must happen here for every purpose that binds to a user — not only for
        // PhoneVerification. Login and PasswordReset codes prove control of the number too.
        if (command.UserId is { } userId)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
                return Result.Failure(Error.NotFound("USER_NOT_FOUND", "User not found."));

            // Promote rather than plain-set, so any pending change is resolved and the account
            // ends up with exactly one verified number.
            user.PromotePendingPhoneNumber(canonicalPhone);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("OTP verified for {Purpose} on phone ending ...{Suffix}",
            command.Purpose, SmsPhoneNumber.Mask(canonicalPhone));

        return Result.Success();
    }
}
