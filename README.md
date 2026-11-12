# EEMP — Education Enterprise Management Platform

Commercial product analysis and implementation blueprint.

**Status:** Analysis and specification baseline — not application implementation.  
**Target stack:** ASP.NET Core Web API + Next.js / TypeScript.  
**Architecture direction:** Modular Monolith, Clean Architecture, API-first, multi-tenant SaaS.

## Start here
1. `AGENTS.md` — mandatory instructions for AI coding agents.
2. `docs/00-governance/PROJECT_CHARTER.md` — scope, goals, product boundaries.
3. `docs/01-product/PRODUCT_REQUIREMENTS.md` — functional and non-functional requirements.
4. `docs/02-architecture/SYSTEM_ARCHITECTURE.md` — proposed technical architecture.
5. `docs/03-domain/DOMAIN_MODEL.md` — core domain concepts.
6. `docs/04-security/MULTI_TENANCY_AND_ISOLATION.md` — tenant and data isolation rules.
7. `docs/08-planning/IMPLEMENTATION_ROADMAP.md` — staged delivery plan.
8. `docs/08-planning/OPEN_DECISIONS.md` — questions that require business/technical approval.

## Important usage rules
- This package is a comprehensive starting baseline, not a claim that every business rule has been finalized.
- Items marked **TBD**, **Assumption**, or **Decision Required** must be resolved before the dependent implementation task.
- Do not silently convert assumptions into approved requirements.
- Do not begin feature implementation until Phase 0 analysis and architecture approval are complete.
- Preserve previously approved security decisions recorded in `docs/04-security/APPROVED_SECURITY_BASELINE.md`.
- The first implementation work package must be independently testable and delivered one task at a time.

## Suggested repository layout
```text
/
├── AGENTS.md
├── CLAUDE.md
├── README.md
├── docs/
│   ├── 00-governance/
│   ├── 01-product/
│   ├── 02-architecture/
│   ├── 03-domain/
│   ├── 04-security/
│   ├── 05-experience/
│   ├── 06-integrations/
│   ├── 07-operations/
│   ├── 08-planning/
│   └── 09-quality/
└── src/                  # created only after architecture approval
```

## Versioning
Use semantic document revisions. Every approved change to a business rule or architecture must be recorded in the decision log with rationale, impact, owner, and date.
