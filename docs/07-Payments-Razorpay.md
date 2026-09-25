# Payments and Razorpay

## Provider abstraction
```text
IPaymentProvider
  -> RazorpayPaymentProvider
```

Application does not depend directly on Razorpay SDK types.

## Supported
- Create payment/order
- Verify payment
- Payment success/failure
- Webhooks
- Refund initiation
- Refund status

## Security
- Never expose Razorpay secret
- Verify webhook signature
- Verify payment server-side
- Store provider IDs
- Use idempotency

## Payment lifecycle
```text
Order
 -> Payment Pending
 -> Razorpay order
 -> Customer payment
 -> Webhook/verification
 -> Confirmed
```

## Refund lifecycle
```text
Refund Requested
 -> Refund Initiated
 -> Refund Pending
 -> Refunded / Failed
```

## Idempotency
Repeated webhook or refund requests must not create duplicate side effects.

## COD
Configurable per store. COD orders must follow a separate payment state path.

## Reconciliation
Store enough provider references to reconcile payments and refunds manually if necessary.
