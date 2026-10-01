namespace KromicCommerce.Application.Features.Auth.SendOtp;

internal sealed class SendOtpHandler(
    IApplicationDbContext db,
    IOtpService otpService,
    ISmsProviderFactory smsProviderFactory,
    IOptions<SmsPolicyOptions> smsPolicyOptions,
    ILogger<SendOtpHandler> logger)
    : ICommandHandler<SendOtpCommand, OtpSendResponse>
{
    /// <summary>
    /// Slack added to the resend cooldown when deciding whether an existing claim is stale.
    /// Covers the gateway HTTP timeout so a slow-but-live send is never overtaken.
    /// </summary>
    private static readonly TimeSpan SendGracePeriod = TimeSpan.FromSeconds(30);

    public async Task<Result<OtpSendResponse>> Handle(
        SendOtpCommand command, CancellationToken cancellationToken)
    {
        var policy = smsPolicyOptions.Value;
        var provider = await smsProviderFactory.CreateAsync(cancellationToken);

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

        // Take the send claim BEFORE reading the cooldown, and hold it until this request has
        // finished writing its row. Two simultaneous requests for the same number and purpose
        // would otherwise both read "no recent code" and both send.
        //
        // The claim is enforced by a UNIQUE index, so it holds across API instances rather than
        // only within one process. Staleness must exceed the worst-case gateway send duration, or
        // a slow-but-live send could be overtaken and two codes delivered.
        var claimStaleAfter = TimeSpan.FromSeconds(policy.ResendCooldownSeconds) + SendGracePeriod;

        var staleBefore = DateTime.UtcNow - claimStaleAfter;
        if (!await db.TryAcquireOtpSendClaimAsync(
                canonicalPhone, command.Purpose, staleBefore, cancellationToken))
        {
            // Another request for this number and purpose is mid-send. Reported as a cooldown:
            // that request will normally leave an OTP row behind, which is exactly the state the
            // cooldown check reports, so the client contract does not change.
            logger.LogInformation("Concurrent OTP send suppressed for {Purpose}.", command.Purpose);

            var resendAt = DateTime.UtcNow.AddSeconds(policy.ResendCooldownSeconds);
            return Result.Failure<OtpSendResponse>(
                Error.Conflict("OTP_COOLDOWN",
                    $"Please wait before requesting another code. Resend available after {resendAt:u}."));
        }

        try
        {
            return await SendUnderClaimAsync(
                command, policy, provider, canonicalPhone, cancellationToken);
        }
        finally
        {
            // Always release, including on cancellation or an unexpected failure. A claim is also
            // reclaimed once stale, so a crash here cannot wedge resends permanently.
            await db.ReleaseOtpSendClaimAsync(
                canonicalPhone, command.Purpose, CancellationToken.None);
        }
    }

    private async Task<Result<OtpSendResponse>> SendUnderClaimAsync(
        SendOtpCommand command,
        SmsPolicyOptions policy,
        ISmsProvider provider,
        string canonicalPhone,
        CancellationToken cancellationToken)
    {
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