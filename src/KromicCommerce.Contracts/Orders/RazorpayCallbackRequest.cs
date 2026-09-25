namespace KromicCommerce.Contracts.Orders;

/// <summary>
/// Payload returned by the Razorpay checkout widget after a successful payment attempt.
/// The backend verifies the signature independently — these values are NOT trusted as-is.
/// Never send payment amount from the frontend; the backend re-validates against the persisted order.
/// </summary>
public sealed record RazorpayCallbackRequest(
    string RazorpayPaymentId,
    string RazorpayOrderId,
    string RazorpaySignature);
