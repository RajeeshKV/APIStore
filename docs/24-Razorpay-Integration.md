# Razorpay Integration

## Purpose

Razorpay is the default online payment provider.

## Abstraction

```text
IPaymentProvider
  -> RazorpayPaymentProvider
```

The Application layer must not depend on Razorpay SDK types.

## Operations

Provider adapter should support:
- Create payment/order
- Verify payment
- Capture/state confirmation where applicable
- Refund
- Refund status
- Webhook event processing

## Configuration

Deployment/customer secrets:
- Key ID
- Key secret
- Webhook secret
- Environment/mode

Never expose Key Secret/Webhook Secret to frontend.

## Payment flow

```text
Checkout
  -> Create local Order
  -> Create Razorpay order
  -> Return safe provider order details
  -> Frontend payment
  -> Provider
  -> Webhook/verification
  -> Idempotency
  -> Update Payment
  -> Confirm Order
  -> Outbox
```

## Verification

Never trust a frontend success callback alone.

Verify provider data server-side and validate webhook signatures.

## Webhooks

Persist provider event ID where available.

Processing:
1. Verify signature
2. Validate payload
3. Check duplicate event
4. Load related payment/order
5. Apply valid state transition
6. Create outbox event
7. Return success

## Idempotency

Duplicate webhook delivery must not:
- Confirm order twice
- Reduce stock twice
- Send duplicate refund
- Send duplicate notification

## Refund

```text
Admin rejection/cancellation
  -> Validate captured amount
  -> Create refund record
  -> Provider refund request
  -> Webhook/status
  -> Finalize refund
```

Never refund more than captured.

## Failure handling

Do not retry an ambiguous financial operation blindly. Reconcile provider status before attempting another financial mutation.

## Testing

Use Razorpay test/sandbox credentials where available. Keep provider-specific integration tests separate from unit tests.
