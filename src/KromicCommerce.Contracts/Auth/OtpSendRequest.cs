namespace KromicCommerce.Contracts.Auth;

public sealed record OtpSendRequest(
    string PhoneNumber,
    /// <summary>Purpose: "PhoneVerification", "Login", or "PasswordReset"</summary>
    string Purpose);
