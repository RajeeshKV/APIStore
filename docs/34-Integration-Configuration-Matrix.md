# Integration Configuration Matrix

| Integration | Interface | Default | Customer selectable | Secrets | Webhook | Outbox |
|---|---|---|---|---|---|---|
| Google OAuth | IIdentityProvider | Google | Yes/enable | Yes | No | No |
| Payments | IPaymentProvider | Razorpay | V1: Razorpay | Yes | Yes | Yes |
| Email | IEmailProvider | Brevo | Managed/customer mode | Yes | No | Yes |
| SMS | ISmsProvider | Configured provider | Yes | Yes | Usually no | Optional |
| Tracking | ITrackingProvider | Configured provider | Yes | Maybe | If supported | Yes |
| Storage | IStorageProvider | Cloudinary | No in V1 | Yes | No | No |
| Real-time | SignalR | SignalR | No | No | No | Yes |
| Database | EF Core/Npgsql | PostgreSQL | Deployment choice | Yes | No | N/A |

## Rule

The application code should depend on interfaces, not provider SDKs.

## Provider selection

Provider selection is configuration-driven, but provider credentials remain deployment/customer secrets.

## Commercial terms

Pricing, free quotas, rate limits, and plan restrictions must be documented separately and verified at implementation time. They must not be encoded as permanent architectural assumptions.
