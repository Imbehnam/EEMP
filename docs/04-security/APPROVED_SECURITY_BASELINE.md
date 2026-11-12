# Approved Security Baseline — Preserve Existing Decisions

This document carries forward previously approved EEMP security and tenancy decisions. Treat these as binding unless the product owner explicitly approves a change.

1. Tenant administrators may manage tenant configuration, institutions, and memberships, but do **not** receive implicit access to student, guardian, teacher, consultation, or financial records.
2. Sibling institutes/branches must remain isolated unless a specific, authorized scope grants access.
3. Supervision/support grants are time-limited to a maximum of 90 days; renewal requires fresh approval.
4. Sensitive aggregate reporting must suppress small cells under 5 where the approved privacy rule applies.
5. Revocation must take effect on the next authorization decision/request; do not rely on stale cached grants.
6. Support access decisions must be recorded and auditable.
7. No secrets or sensitive personal data may be embedded in documentation, source control, logs, or prompts.
8. Platform support and operational access are separated from tenant business-data access.

These rules must have automated authorization and tenant-isolation tests.
