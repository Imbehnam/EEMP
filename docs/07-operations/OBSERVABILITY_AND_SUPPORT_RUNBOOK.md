# Observability and Support Runbook

## Signals
- API latency/error rate/throughput.
- Database connection, query latency, deadlocks, storage.
- Queue depth, job lag, retries, dead letters.
- Payment success/failure and reconciliation mismatch.
- SMS delivery and reminder lateness.
- Video meeting creation failures.
- Tenant quota and storage usage.
- Authentication failures and suspicious authorization denials.

## Operational dashboards
Platform health; tenant health; integration health; job queue; financial reconciliation; notification delivery; security events; subscription/usage.

## Support process
Ticket intake → tenant verification → purpose/authorization → time-limited grant if needed → monitored action → revoke → audit review. Support must not use shared generic accounts.

## Incident response
Severity levels, on-call ownership, containment, evidence preservation, customer communication, recovery, postmortem, and corrective actions.
