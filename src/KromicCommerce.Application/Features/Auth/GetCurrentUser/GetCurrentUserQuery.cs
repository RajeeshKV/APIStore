using KromicCommerce.Contracts.Auth;

namespace KromicCommerce.Application.Features.Auth.GetCurrentUser;

public sealed record GetCurrentUserQuery(Guid UserId) : IQuery<MeResponse>;
