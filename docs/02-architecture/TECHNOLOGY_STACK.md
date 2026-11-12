# Technology Stack — Proposed Baseline

| Concern | Proposed choice | Notes |
|---|---|---|
| Backend | ASP.NET Core Web API | Use supported LTS release selected at implementation start |
| ORM | EF Core | Explicit migrations, query review, concurrency tokens |
| Database | SQL Server | Validate hosting, cost, RLS, and operational expertise |
| Frontend | Next.js + TypeScript | App Router; typed API client |
| UI | Accessible component system | RTL/LTR, keyboard support, design tokens |
| Localization | next-intl or equivalent | Translation catalogs, locale routing, formatting |
| Cache | Redis | Cache and distributed coordination, never sole source of truth |
| Jobs | Durable scheduler | Hangfire or equivalent after operational evaluation |
| Messaging reliability | Transactional outbox/inbox | Required for external side effects |
| Files | S3-compatible object storage | Private buckets, signed access, malware scanning |
| Observability | OpenTelemetry + structured logs | Correlation IDs, metrics, traces |
| API docs | OpenAPI | Contract generation and compatibility checks |
| Containers | Docker | Reproducible dev/test/prod images |
| CI/CD | GitHub Actions or equivalent | Build, test, scan, migration validation |
| Testing | xUnit + integration tests; Playwright | Contract, security, E2E coverage |

## Selection gates
Before locking versions/providers, verify support lifecycle, security advisories, licensing, hosting region, data residency, cost, vendor lock-in, and team capability.
