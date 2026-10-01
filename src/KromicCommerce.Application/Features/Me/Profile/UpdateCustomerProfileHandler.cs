using Microsoft.Extensions.Options;
using KromicCommerce.Application.Options;

namespace KromicCommerce.Application.Features.Me.Profile;

internal sealed class UpdateCustomerProfileHandler(
    IApplicationDbContext db,
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

            // A self-declared number is never verified on the way in. With SMS configured the
            // customer must prove control; with SMS off the number is still accepted and used
            // for contact, it just cannot satisfy a checkout verification requirement.
            user.SetPhoneNumber(canonical, verified: false);
        }

        await db.SaveChangesAsync(ct);

        return Result.Success(new CustomerProfileResponse(
            user.Id, user.Email,
            user.FirstName, user.LastName,
            profile.DisplayName,
            user.PhoneNumber,
            user.PhoneNumberVerified,
            profile.AvatarUrl,
            profile.DateOfBirth,
            profile.NewsletterConsent,
            profile.PreferredTimeZoneId,
            user.LastLoginAt,
            profile.UpdatedAtUtc));
    }
}