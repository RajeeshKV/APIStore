namespace KromicCommerce.Application.Features.Me.Profile;

public sealed record GetCustomerProfileQuery(Guid UserId) : IQuery<CustomerProfileResponse>;
