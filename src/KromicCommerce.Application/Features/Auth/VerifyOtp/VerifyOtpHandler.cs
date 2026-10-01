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

    /// <summary>
    /// The code was genuine but belongs to a number the account has since moved away from. Kept
    /// distinct from <see cref="InvalidOtp"/> so a client can tell "start the current flow again"
    /// apart from "that code was wrong", and so no phone state is touched either way.
    /// </summary>
    private static readonly Error StalePhoneVerification = Error.Conflict(
        "PHONE_VERIFICATION_NOT_PENDING",
        "This verification code is for a phone number that is no longer being verified. Request a new code.");

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

        // Only PhoneVerification may change the account phone.
        //
        // Proving control of a number for one operation is not the same as designating it the
        // account phone, and conflating them let a Login or PasswordReset code silently overwrite
        // a number the customer never asked to change. A stale code is also refused below, so an
        // abandoned change cannot be reinstated by replaying its still-valid OTP.
        if (command.Purpose == OtpPurpose.PhoneVerification && command.UserId is { } userId)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
                return Result.Failure(Error.NotFound("USER_NOT_FOUND", "User not found."));

            // The code proves the number, not that this number is still the one being verified. If
            // the customer has since requested a different one, the verified number and the pending
            // number both stay exactly as they are, and this code is not marked used.
            if (!user.TryCompletePhoneVerification(canonicalPhone))
            {
                logger.LogInformation(
                    "Refused stale phone verification for phone ending ...{Suffix}; pending number has changed.",
                    SmsPhoneNumber.Mask(canonicalPhone));
                return Result.Failure(StalePhoneVerification);
            }
        }

        // Spent in every successful case, so a code cannot be replayed. Reaching here without the
        // branch above simply means the purpose was not PhoneVerification, and no profile state
        // moved — that operation's own flow is responsible for what it does next.
        otpRequest.MarkVerified();

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("OTP verified for {Purpose} on phone ending ...{Suffix}",
            command.Purpose, SmsPhoneNumber.Mask(canonicalPhone));

        return Result.Success();
    }
}
