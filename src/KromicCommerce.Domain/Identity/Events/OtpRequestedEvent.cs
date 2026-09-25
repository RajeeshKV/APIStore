namespace KromicCommerce.Domain.Identity.Events;

public sealed class OtpRequestedEvent(Guid otpRequestId, string phoneNumber, OtpPurpose purpose) : DomainEvent
{
    public Guid OtpRequestId { get; } = otpRequestId;
    public string PhoneNumber { get; } = phoneNumber;
    public OtpPurpose Purpose { get; } = purpose;
}
