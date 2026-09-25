namespace KromicCommerce.Application.Features.Checkout;

public sealed record HandlePaymentWebhookCommand(
    string RawPayload,
    string Signature,
    string Provider) : ICommand;
