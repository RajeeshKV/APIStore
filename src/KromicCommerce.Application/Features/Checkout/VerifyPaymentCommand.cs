namespace KromicCommerce.Application.Features.Checkout;

public sealed record VerifyPaymentCommand(
    Guid OrderId,
    Guid CustomerId,
    string RazorpayPaymentId,
    string RazorpayOrderId,
    string RazorpaySignature) : ICommand<PaymentResponse>;
