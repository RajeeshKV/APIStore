---
inclusion: fileMatch
fileMatchPattern:
  - "**/*.cs"
  - "**/*.json"
  - ".env*"
  - "Dockerfile"
  - "docker-compose*"
---

# Configuration Rules

Infrastructure/deployment configuration must come from environment variables or strongly typed configuration bound from environment variables.

Examples:
- Database
- JWT
- Google OAuth
- Razorpay
- Brevo
- Cloudinary
- SMS
- Tracking
- CORS
- URLs
- logging
- rate limits
- cache
- background workers
- webhook secrets

Use Options classes and startup validation.

Business settings do NOT belong in environment variables. They belong in the database/Admin.

Never commit real secrets.

`.env.example` must contain every required environment key with safe placeholders.
`.env` must be gitignored.
