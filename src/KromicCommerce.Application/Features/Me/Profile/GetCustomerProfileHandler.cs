namespace KromicCommerce.Application.Features.Me.Profile;

internal sealed class GetCustomerProfileHandler(IApplicationDbContext db)
    : IQueryHandler<GetCustomerProfileQuery, CustomerProfileResponse>
{
    public async Task<Result<CustomerProfileResponse>> Handle(
        GetCustomerProfileQuery query, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == query.UserId, ct);
        if (user is null)
            return Result.Failure<CustomerProfileResponse>(
                Error.NotFound("USER_NOT_FOUND", "User not found."));

        var profile = await db.CustomerProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == query.UserId, ct);

        return Result.Success(new CustomerProfileResponse(
            user.Id, user.Email,
            user.FirstName, user.LastName,
            profile?.DisplayName,
            profile?.PhoneNumber ?? user.PhoneNumber,
            user.PhoneNumberVerified,
            profile?.AvatarUrl,
            profile?.DateOfBirth,
            profile?.NewsletterConsent ?? false,
            profile?.PreferredTimeZoneId,
            user.LastLoginAt,
            profile?.UpdatedAtUtc ?? user.UpdatedAtUtc));
    }
}
