# Deployment and Operations

## Deployment model
Each customer gets:
- Frontend deployment
- API deployment
- PostgreSQL
- Customer domain
- Customer-specific secrets

## Environment
- Development
- Staging/test
- Production

## Environment configuration
Use environment variables/secret store for:
- Database connection
- JWT signing key
- Google OAuth secret
- Razorpay secret
- Brevo API key
- SMS credentials
- Tracking credentials
- Cloudinary credentials

## Database
- Run EF migrations in a controlled manner
- Backups
- Restore procedure
- Index review
- Connection pooling

## Health
Expose:
- GET /health
- HEAD /health

Check application/database. External providers should be checked selectively so transient provider failure does not falsely make the application unhealthy unless required.

## Logging
Serilog structured logs with:
- Timestamp
- Level
- Trace/correlation ID
- Request information
- User ID where safe
- Provider event IDs
- Duration

## CI/CD
Pipeline should:
1. Restore
2. Build
3. Run tests
4. Publish
5. Build container
6. Deploy
7. Run/verify migrations according to deployment policy
8. Health-check
9. Smoke-test

## Docker
Multi-stage build.
Run as non-root where practical.
Use production configuration.

## Backups
Automated PostgreSQL backup strategy and documented restore process are mandatory before production.

## Rollback
Keep deployable previous version and database migration rollback strategy. Prefer backward-compatible migrations for zero/low downtime changes.
