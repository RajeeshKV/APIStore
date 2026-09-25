using KromicCommerce.Contracts.Auth;
using Microsoft.EntityFrameworkCore;

namespace KromicCommerce.Application.Features.Auth.GetCurrentUser;

internal sealed class GetCurrentUserHandler(IApplicationDbContext db)
    : IQueryHandler<GetCurrentUserQuery, MeResponse>
{
    public async Task<Result<MeResponse>> Handle(
        GetCurrentUserQuery query,
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == query.UserId, cancellationToken);

        if (user is null)
            return Result.Failure<MeResponse>(Error.NotFound("USER_NOT_FOUND", "User not found."));

        return Result.Success(new MeResponse(
            Id: user.Id,
            Email: user.Email,
            FirstName: user.FirstName,
            LastName: user.LastName,
            PhoneNumber: user.PhoneNumber,
            PhoneNumberVerified: user.PhoneNumberVerified,
            Role: user.Role.ToString(),
            IsActive: user.IsActive,
            EmailVerifiedAt: user.EmailVerifiedAt,
            LastLoginAt: user.LastLoginAt));
    }
}
