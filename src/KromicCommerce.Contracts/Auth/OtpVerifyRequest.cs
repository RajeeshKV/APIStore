namespace KromicCommerce.Contracts.Auth;

public sealed record OtpVerifyRequest(
    string PhoneNumber,
    string Otp,
    string Purpose);
