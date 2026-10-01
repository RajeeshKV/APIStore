using Microsoft.Extensions.Options;
using KromicCommerce.Application.Options;

namespace KromicCommerce.Application.Features.Me.Profile;

internal sealed class UpdateCustomerProfileHandler(
    IApplicationDbContext db,
    ISmsProviderFactory smsProviderFactory,
    IOptions<SmsPolicyOptions> smsPolicyOptions)
    : ICommandHandler<UpdateCustomerProfileCommand, CustomerProfileResponse>
{
    public async Task<Result<CustomerProfileResponse>> Handle(
        UpdateCustomerProfileCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct);
        if (user is null)
            return Result.Failure<CustomerProfileResponse>(
                Error.NotFound("USER_NOT_FOUND", "User not found."));

        var profile = await db.CustomerProfiles
            .FirstOrDefaultAsync(p => p.UserId == command.UserId, ct);

        if (profile is null)
        {
            profile = CustomerProfile.Create(command.UserId);
            db.CustomerProfiles.Add(profile);
        }

        profile.UpdateProfile(
            command.DisplayName,
            command.DateOfBirth,
            command.PhoneNumber,
            command.NewsletterConsent,
            command.PreferredTimeZoneId);

        // The account phone on User is the only phone the rest of the system trusts — checkout,
        // verification status, and address auto-fill all read it. The profile column is kept
        // in sync for backward compatibility but is never read back as the contact number.
        if (!string.IsNullOrWhiteSpace(command.PhoneNumber))
        {
            var canonical = SmsPhoneNumber.TryToE164(command.PhoneNumber) ?? command.PhoneNumber.Trim();

            // Whether SMS can actually deliver decides what a submitted number means.
            var smsOperational = await smsProviderFactory.GetStatusAsync(ct);

            if (smsOperational.IsConfigured)
            {
                // A self-declared number is never verified on the way in. It is held as pending
                // and only becomes the account number once an OTP sent to it is accepted, so a
                // change in progress cannot strip an existing verified number.
                user.RequestPhoneNumberChange(canonical);
            }
            else
            {
                // With SMS unavailable there is no way to prove ownership, so a verification
                // requirement could never be satisfied. Preserve the existing behaviour: accept
                // the number for contact, simply unverified.
                user.SetPhoneNumber(canonical, verified: false);
            }
        }

        await db.SaveChangesAsync(ct);

        return Result.Success(new CustomerProfileResponse(
            user.Id, user.Email,
            user.FirstName, user.LastName,
            profile.DisplayName,
            user.PhoneNumber,
            user.PhoneNumberVerified,
            user.PendingPhoneNumber,
            profile.AvatarUrl,
            profile.DateOfBirth,
            profile.NewsletterConsent,
            profile.PreferredTimeZoneId,
            user.LastLoginAt,
            profile.UpdatedAtUtc));
    }
}