# Deployment and Environments

## Environments
Local development, CI, staging/UAT, production. Never share production secrets with local or CI environments.

## Deployment components
Next.js web, ASP.NET Core API, background worker, SQL Server, Redis, object storage, reverse proxy/ingress, secret manager, telemetry stack.

## Requirements
- Containerized repeatable builds.
- Health/readiness/liveness endpoints.
- Database migration deployment strategy with backup and rollback/forward-fix plan.
- Zero/low-downtime deployment strategy for API compatibility.
- Environment-specific configuration.
- TLS and secure headers.
- Network segmentation and least-privilege service accounts.
- Scheduled backup and restore validation.
- Centralized logs, metrics, traces, alerting.
- Infrastructure as code before production scale.
