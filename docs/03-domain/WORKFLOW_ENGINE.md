# Workflow Engine Specification

## Purpose
Provide configurable, auditable business processes without hard-coding each request lifecycle.

## Definition components
- Workflow type and tenant scope.
- Version number and lifecycle (draft, published, retired).
- Form schema and validation rules.
- States and transitions.
- Roles/permissions allowed to act.
- Assignment strategy (named user, role queue, branch queue, round-robin/manual).
- Conditions and guards.
- SLA duration/calendar, escalation and reminders.
- Required approvals and separation-of-duty rules.
- Notification templates and document requirements.

## Runtime
Each instance pins a published definition version. Commands validate current state, actor permission, tenant/branch/resource scope, required data, and concurrency token. Every transition writes immutable history with actor, timestamp, reason, and payload diff/reference.

## Required capabilities
Submit, assign, claim, comment, request information, return, approve, reject, cancel, reassign, escalate, close, reopen under policy, and withdraw.

## Safety
- No arbitrary code execution in workflow expressions.
- Use a constrained expression language / declarative conditions.
- Version and test workflow changes before publishing.
- Existing instances are not silently migrated.
- Prevent self-approval where policy requires.
- Enforce idempotency for retried commands.
- Attachments use private storage and malware scanning.
- Audit administrative edits to workflow definitions.
