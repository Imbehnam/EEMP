# Cross-Institute Referral and Data Transfer

## Core principle
A referral is a controlled disclosure transaction, not a shared unrestricted lookup.

## Flow
Initiated → AwaitingConsent/Approval → Sent → DestinationReview → Accepted/Rejected → Imported/Completed; may expire or be revoked according to policy.

## Required controls
- Verify source and destination tenant/institute identities.
- Verify actor permission at both ends.
- Capture purpose and legal basis.
- Capture student/guardian consent or other lawful authority where required.
- Select data categories and fields explicitly.
- Minimize data; exclude sensitive notes by default.
- Encrypt transfer package and use expiring access.
- Record destination acceptance and import mapping.
- Preserve source provenance and timestamps.
- Support correction, revocation, expiry, and audit.
- Prevent destination from browsing source tenant after transfer.
- Define duplicate matching and conflict resolution.
- Define whether transfer is copy, move, or referral-only; do not assume.
