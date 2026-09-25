namespace KromicCommerce.Application.Features.Me.Profile;

internal sealed class UpdateCustomerProfileHandler(IApplicationDbContext db)
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

        await db.SaveChangesAsync(ct);

        return Result.Success(new CustomerProfileResponse(
            user.Id, user.Email,
            user.FirstName, user.LastName,
            profile.DisplayName,
            profile.PhoneNumber ?? user.PhoneNumber,
            user.PhoneNumberVerified,
            profile.AvatarUrl,
            profile.DateOfBirth,
            profile.NewsletterConsent,
            profile.PreferredTimeZoneId,
            user.LastLoginAt,
            profile.UpdatedAtUtc));
    }
}
