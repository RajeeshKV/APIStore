using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using KromicCommerce.Application.Options;
using KromicCommerce.Contracts.Auth;

namespace KromicCommerce.Application.Features.Auth.PhoneVerificationStatus;

/// <summary>
/// Reports whether this customer must verify a phone number before checking out.
///
/// This is the backend contract the UI gates on. It deliberately derives the requirement from
/// live configuration rather than from a client-supplied flag, so a client cannot opt itself
/// out of verification by asking a different question.
/// </summary>
internal sealed class GetPhoneVerificationStatusHandler(
    IApplicationDbContext db,
    ISmsProviderFactory smsProviderFactory,
    IOptions<SmsPolicyOptions> smsPolicyOptions)
    : IQueryHandler<GetPhoneVerificationStatusQuery, PhoneVerificationStatusResponse>
{
    public async Task<Result<PhoneVerificationStatusResponse>> Handle(
        GetPhoneVerificationStatusQuery query, CancellationToken ct)
    {
        var policy = smsPolicyOptions.Value;
        var status = await smsProviderFactory.GetStatusAsync(ct);

        // Requirement = policy asks for it AND a provider can actually deliver a code.
        // With SMS off, verification is impossible, so requiring it would lock every customer out.
        var required = policy.RequireVerifiedPhoneAtCheckout && status.IsConfigured;

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == query.UserId, ct);

        if (user is null)
        {
            return Result.Failure<PhoneVerificationStatusResponse>(
                Error.NotFound("USER_NOT_FOUND", "User not found."));
        }

        var phone = SmsPhoneNumber.TryToE164(user.PhoneNumber);
        var verified = phone is not null && user.PhoneNumberVerified;

        return Result.Success(new PhoneVerificationStatusResponse(
            VerificationRequired: required,
            PhoneNumber: phone,
            Verified: verified,
            PendingPhoneNumber: SmsPhoneNumber.TryToE164(user.PendingPhoneNumber),
            VerificationSatisfied: !required || verified,
            OtpLength: policy.Length,
            OtpExpiryMinutes: policy.ExpiryMinutes,
            ResendCooldownSeconds: policy.ResendCooldownSeconds));
    }
}
