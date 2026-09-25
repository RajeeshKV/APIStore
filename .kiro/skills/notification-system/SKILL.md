---
name: notification-system
description: Use when implementing Brevo email, SMS OTP, SignalR notifications, notification workers, or notification-related outbox handlers.
---

# Notification System

Read only the relevant email/SMS/SignalR documents.

Rules:
- Email/SMS are provider abstractions.
- Email templates are generic code-owned templates with configurable business branding.
- Credentials are environment configuration.
- Long-running notification work belongs behind the Outbox/background worker.
- SignalR publishes after committed state changes.
- SignalR is not the source of truth.
- Clients reconnect and reload authoritative state.
- Never send OTPs through logs.
- Provider failure must not roll back an already-committed commerce transaction.
