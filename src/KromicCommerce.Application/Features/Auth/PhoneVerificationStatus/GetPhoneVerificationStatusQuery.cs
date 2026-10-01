using KromicCommerce.Contracts.Auth;

namespace KromicCommerce.Application.Features.Auth.PhoneVerificationStatus;

public sealed record GetPhoneVerificationStatusQuery(Guid UserId)
    : IQuery<PhoneVerificationStatusResponse>;
