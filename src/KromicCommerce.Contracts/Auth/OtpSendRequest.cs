using KromicCommerce.Domain.Identity;

namespace KromicCommerce.Contracts.Auth;

public sealed record OtpSendRequest(
    string PhoneNumber,
    OtpPurpose Purpose);
