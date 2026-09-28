using KromicCommerce.Domain.Identity;

namespace KromicCommerce.Contracts.Auth;

public sealed record OtpVerifyRequest(
    string PhoneNumber,
    string Otp,
    OtpPurpose Purpose);
