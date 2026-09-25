namespace KromicCommerce.Application.Features.Auth.VerifyOtp;

public sealed record VerifyOtpCommand(
    string PhoneNumber,
    string SubmittedOtp,
    OtpPurpose Purpose,
    Guid? UserId) : ICommand;
