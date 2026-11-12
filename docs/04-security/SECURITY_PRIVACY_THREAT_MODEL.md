# Security, Privacy, and Threat Model

## Assets
Student/guardian PII, grades, attendance, counseling notes, teacher records, financial records, credentials, access tokens, meeting links, uploaded documents, video media, tenant configuration, and audit evidence.

## Threats
Cross-tenant IDOR; privilege escalation; compromised guardian account; malicious staff; insecure referral; forged payment callback; replayed webhook; duplicate payment; leaked media URL; unsafe file upload; workflow abuse; SMS abuse; session theft; injection; XSS/CSRF; denial of service; backup exposure; insider support access.

## Controls
- Secure authentication, MFA for privileged users, rate limiting, secure cookies/token storage.
- Server-side policy authorization and resource checks.
- Tenant/branch isolation and RLS defense-in-depth.
- Encryption in transit and at rest; managed keys.
- Secrets manager and rotation.
- Signed webhook verification and idempotency.
- Private object storage, file scanning, content-type/size validation.
- CSP, output encoding, CSRF protections where applicable.
- Audit privileged reads/writes and support access.
- Data minimization, consent, retention, export, correction, deletion workflows.
- Secure backups and restore tests.
- Dependency and container scanning.
- Incident response and breach notification process.

## Privacy governance
Determine applicable laws and roles (controller/processor), lawful basis, minors' consent/guardian authority, data residency, retention periods, data subject rights, cross-border transfers, and breach timelines before launch in each market.

## Sensitive data
Counseling and academic planning notes require separate data classification and more restrictive access. Do not include such notes in general search, broad exports, or aggregate analytics by default.
