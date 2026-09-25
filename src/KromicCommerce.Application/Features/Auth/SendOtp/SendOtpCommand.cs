namespace KromicCommerce.Application.Features.Auth.SendOtp;

public sealed record SendOtpCommand(
    string PhoneNumber,
    OtpPurpose Purpose,
    Guid? UserId) : ICommand<OtpSendResponse>;
