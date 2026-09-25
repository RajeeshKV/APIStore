namespace KromicCommerce.Contracts.Auth;

/// <summary>Admin requests a password reset email. Always returns the same generic message.</summary>
public sealed record RequestPasswordResetRequest(string Email);

/// <summary>Admin submits the token received by email along with the new password.</summary>
public sealed record ResetPasswordRequest(
    string Email,
    string Token,
    string NewPassword,
    string ConfirmPassword);
