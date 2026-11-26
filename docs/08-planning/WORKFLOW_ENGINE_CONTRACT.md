# EEMP-011 — Workflow engine contract (backend)

Status: implemented on branch `feature/workflow-engine-backend` (stacked on `feature/report-card-pdf`, draft PR #6).
Decisions: **DL-070..DL-074** (numbering deliberately starts at DL-070: the parallel Finance work, EEMP-014 on
`feature/m3-finance-backend`, uses DL-059..DL-066+). Everything in the M1/M2 contracts still holds; this document only adds to it.
Scope follows OD-027 (accepted assumption, pending confirmation): states / transitions / permission-based approvers / SLA now;
conditions, parallel approval, escalation and delegation later.

## 1. Model (DL-070)

Module `Workflow`, own schema `workflow`, own migrations history. All tables tenant-owned (central query filter, tenant-leading
indexes, tenant-composite FKs).

| Table | Purpose |
|---|---|
| `Definitions` | One per tenant + `key` (slug, unique per tenant). Binds a process to a subject kind: `subjectModule` + `subjectType`. `isActive`, `publishedVersion`. |
| `DefinitionVersions` | `versionNo` 1..n, `status` `draft` → `published` → `retired`, `requireDistinctApprovers`. Only drafts are editable; publishing retires the previous published version. |
| `DefinitionSteps` | Ordered approval steps of a version: `order`, `name`, `permission` (catalog code), optional `assigneeUserId`, optional `slaHours`, `allowReturn`. |
| `Instances` | One run of a published version bound to a subject: `subjectModule/subjectType/subjectId`, optional `branchId`, `title`, `requestedByUserId`, `status`, `currentStep*` (denormalised for the inbox), `currentStepDueAt`, `idempotencyKey`, `rowVersion`. Pins `versionId`/`versionNo`. |
| `InstanceHistory` | Append-only transitions: `sequence`, `action`, `fromStatus`, `toStatus`, `stepOrder`, `actorUserId`, `comment`, `requestId`, `occurredAt`. |
| `OutboxMessages` | Integration events written **in the same SaveChanges** as the transition that caused them; delivered at-least-once to the owning module's handler. |

### States and transitions (fixed machine; steps are configurable)
Definition steps are a sequential approval chain. Instance status: `pending` (at step *n*), `returned`, `approved`, `rejected`,
`cancelled` (the last three are terminal).

| From | Action | Who | To |
|---|---|---|---|
| — | start | owning module (in-process `IWorkflowService`) | `pending` @ step 1 |
| `pending` @ n | `approve` | eligible approver of step n | `pending` @ n+1, or `approved` after the last step |
| `pending` @ n | `reject` (comment required) | eligible approver of step n | `rejected` |
| `pending` @ n | `return` (comment required; step must have `allowReturn`) | eligible approver of step n | `returned` |
| `returned` | `resubmit` | requester | `pending` @ step 1 (approvals restart) |
| `pending` / `returned` | `cancel` | requester; holder of `workflow.instances.manage` in the instance branch; owning module (system) | `cancelled` |
| terminal | any | — | `409 workflow.invalid_transition` |

Every transition writes one `InstanceHistory` row and the generic audit trail (field names only). Instances never migrate to a
newer definition version.

### Approver resolution (DL-071)
A user is an eligible approver of the current step when **all** hold:
1. They hold the step's `permission` **in the instance's branch** (branch-scoped or tenant-wide grant); for an instance with no
   branch (`branchId = null`, tenant-level subject) a **tenant-wide** grant is required.
2. If the step has `assigneeUserId`, they are that user (the named user must *also* hold the permission — naming a user never grants
   access by itself, so a definition editor cannot escalate anyone).
3. Maker-checker: they are **not the requester** (`403 workflow.self_approval_forbidden`).
4. When the version has `requireDistinctApprovers` (default true), they have not approved an earlier step of the current round
   (`403 workflow.approver_already_acted`).

Grants are re-read per request (EEMP-005), so revocation applies immediately; resolution happens at action/inbox time, not at start.

Not available (documented gaps):
- **Role-based steps**: roles are tenant-defined bundles; steps target a permission instead (give the role that permission).
- **Requester's manager**: EEMP has no reporting-line / org-hierarchy data yet (People has no manager relation). A future
  `assignment: requesterManager` strategy needs that data first.
- Round-robin/claim, delegation, parallel/quorum approval, conditions/guards, escalation and SLA reminders (EEMP-017).

### SLA
`slaHours` per step (1..8760, optional). `currentStepDueAt = enteredStepAt + slaHours` (UTC). Inbox/detail expose `dueAt` and
`isOverdue`. No automatic escalation or reminders yet.

## 2. Permissions (DL-072)

| Code | Sensitive | Branch-scoped | Use |
|---|---|---|---|
| `workflow.definitions.read` | false | false | list/read definitions |
| `workflow.definitions.manage` | false | false | create/edit/publish definitions (tenant configuration) |
| `workflow.instances.read` | true | true | oversight list/detail of all instances in the branch |
| `workflow.instances.manage` | true | true | administrative cancel of any instance in the branch |
| `workflow.tasks.approve` | true | true | generic approver permission for steps that have no module-specific approval permission |

Built-in roles: **TenantAdmin** gets `workflow.definitions.read/manage` only (no instance data — instances reference student or
financial subjects, security baseline #1). **BranchManager** gets `workflow.definitions.read`, `workflow.instances.read`,
`workflow.instances.manage`, `workflow.tasks.approve`. Existing tenants receive these through the Identity startup system-role sync
that the Finance work adds (DL-064 on the finance branch); until both branches are merged, tenants created before this change keep
their old system-role grants (dev: recreate the DB).

A step's `permission` must exist in the catalog (`validation.unknown_permission`). Module-specific approval permissions (e.g.
`finance.approve`) are the expected choice for module processes.

## 3. HTTP API (`/api/v1/workflow`, camelCase JSON, problem+json errors)

Definitions:
- `GET /workflow/definitions` (`workflow.definitions.read`) → `[{ id, key, name, subjectModule, subjectType, isActive, publishedVersion?, latestVersion, rowVersion }]`
- `POST /workflow/definitions` (`manage`) `{ key, name, subjectModule, subjectType, requireDistinctApprovers?, steps: [step] }` → 201 detail, version 1 `draft`.
- `GET /workflow/definitions/{id}` → detail `{ …summary, versions: [{ versionNo, status, requireDistinctApprovers, publishedAt?, rowVersion, steps: [{ order, name, permission, assigneeUserId?, slaHours?, allowReturn }] }] }`
- `PUT /workflow/definitions/{id}` (`manage`) `{ name, isActive, rowVersion }`
- `POST /workflow/definitions/{id}/versions` (`manage`) → new draft copied from the latest version (`409 workflow.draft_exists` when a draft exists).
- `PUT /workflow/definitions/{id}/versions/{versionNo}` (`manage`) `{ requireDistinctApprovers, steps, rowVersion }` — drafts only (`409 workflow.version_not_draft`).
- `POST /workflow/definitions/{id}/versions/{versionNo}/publish` (`manage`) `{ rowVersion }` (definition rowVersion) → previous published version becomes `retired`.
- step: `{ name (1..200), permission, assigneeUserId?, slaHours?, allowReturn (default true) }`; 1..10 steps (`validation.required` on `steps`).
- `key`/`subjectModule`/`subjectType`: `^[a-z][a-z0-9_.-]{1,49}$`; key unique per tenant (`409 workflow.definition_key_taken`).

Tasks / instances:
- `GET /workflow/inbox?page&pageSize&definitionKey&overdue` (any tenant user) → paged `[instanceSummary]` where the caller is an
  eligible approver of the current step (maker-checker and distinct-approver rules applied), oldest due first.
- `GET /workflow/instances?scope=mine|all&status&definitionKey&subjectModule&subjectType&subjectId&page&pageSize` — `mine` (default):
  instances the caller requested; `all`: requires `workflow.instances.read`, limited to its branch scope (tenant-level instances need
  a tenant-wide grant).
- `GET /workflow/instances/{id}` → detail with `history` and `allowedActions` (for the caller). Visible to the requester, eligible
  approvers, anyone who acted on it, or `workflow.instances.read` in scope; otherwise `404`.
- `POST /workflow/instances/{id}/approve|reject|return|resubmit|cancel` `{ rowVersion, comment?, requestId? }` → detail.
  - `rowVersion` required (stale → `409 concurrency.conflict`).
  - `requestId` (optional, ≤64 chars, client-generated): retrying the same command with the same `requestId` returns the current state
    without applying it twice (idempotent retries).
  - `comment` ≤ 2000 chars; required for `reject` and `return` (`validation.required`).

instanceSummary: `{ id, definitionId, definitionKey, definitionName, versionNo, subjectModule, subjectType, subjectId, branchId?, title,
requestedByUserId, status, currentStep?: { order, name, permission, assigneeUserId?, dueAt?, isOverdue }, startedAt, completedAt?, rowVersion }`.
history item: `{ sequence, action: started|approved|rejected|returned|resubmitted|cancelled, fromStatus?, toStatus, stepOrder?, stepName?, actorUserId?, comment?, occurredAt }`.

There is **no HTTP start endpoint**: instances are started only by the module that owns the subject (it validates the subject and its
own permissions first). This keeps users from attaching approvals to subjects they cannot see.

## 4. In-process integration (DL-073)

`EEMP.Modules.Workflow.Contracts`:

```csharp
public interface IWorkflowService
{
    // Starts an instance of the tenant's published definition `DefinitionKey`. Requester = the authenticated user
    // (RequestedByUserId is only used when the call runs without a user, e.g. a system job).
    // Idempotent on (subjectModule, IdempotencyKey): a retry returns the existing instance.
    Task<WorkflowInstanceInfo> StartAsync(StartWorkflowCommand command, CancellationToken ct = default);
    Task<WorkflowInstanceInfo?> FindAsync(Guid instanceId, CancellationToken ct = default);
    Task<WorkflowInstanceInfo?> FindActiveAsync(string subjectModule, string subjectType, Guid subjectId, CancellationToken ct = default);
    // System cancel by the owning module (e.g. the subject was deleted). No-op on terminal instances.
    Task CancelAsync(Guid instanceId, string subjectModule, string? reason, CancellationToken ct = default);
}

public sealed record StartWorkflowCommand(string DefinitionKey, string SubjectModule, string SubjectType, Guid SubjectId,
    Guid? BranchId, string Title, string IdempotencyKey, Guid? RequestedByUserId = null);

public interface IWorkflowEventHandler   // registered by the owning module (DI, many allowed)
{
    string SubjectModule { get; }
    Task HandleAsync(WorkflowEvent e, CancellationToken ct);
}

public sealed record WorkflowEvent(Guid EventId, Guid TenantId, Guid InstanceId, string DefinitionKey, int VersionNo,
    string SubjectModule, string SubjectType, Guid SubjectId, Guid? BranchId, string EventType /* completed|returned */,
    string Outcome /* approved|rejected|cancelled|returned */, Guid? ActorUserId, Guid RequestedByUserId, DateTime OccurredAt);
```

Rules:
- `StartAsync` validates: definition exists, is active and published (`422 workflow.definition_not_published`), its
  `subjectModule/subjectType` equal the command's (`422 workflow.subject_mismatch`), branch exists, no other active instance of the
  same definition for the subject (`409 workflow.active_instance_exists`), title 1..200 chars. Same idempotency key for another
  subject → `409 workflow.subject_mismatch`.
- **Outbox**: terminal transitions and `return` write a `WorkflowEvent` into `workflow.OutboxMessages` in the same transaction.
  A dispatcher delivers it after the request commits (inline, best-effort) and a background worker retries leftovers
  (`Workflow:Outbox:WorkerEnabled` default true, `PollSeconds` 15, exponential backoff, `failed` after 10 attempts). Each
  delivery runs in a fresh DI scope inside the event's tenant scope, with **no user** (system context).
- Delivery is **at-least-once**. Handlers must be idempotent: key on `EventId` (inbox table or a guarded state change such as
  `UPDATE … WHERE status = 'pendingApproval' AND workflowInstanceId = @InstanceId`), and ignore events for instances they did not
  start. A message is claimed with an optimistic lock, so two dispatchers never deliver it concurrently; a handler exception
  leaves it pending for retry. Only the exception type name is stored (no PII in logs or the outbox).
- Starting is not atomic with the module's own write (separate DbContexts). Pattern: save the subject as `pendingApproval` with
  `idempotencyKey = "<subjectType>:<subjectId>:<attempt>"`, then call `StartAsync`; on failure retry with the same key (idempotent)
  or roll the subject back.

## 5. Error codes (localized fa/en/tr/ar in `messages.*.json`)

`workflow.definition_key_taken` (409), `workflow.definition_inactive` (422), `workflow.definition_not_published` (422),
`workflow.version_not_draft` (409), `workflow.draft_exists` (409), `workflow.invalid_transition` (409, params `from`, `action`),
`workflow.self_approval_forbidden` (403), `workflow.approver_already_acted` (403), `workflow.not_eligible` (403),
`workflow.return_not_allowed` (422), `workflow.active_instance_exists` (409), `workflow.subject_mismatch` (409/422),
field codes `workflow.unknown_assignee`, `workflow.branch_not_found`. Plus the existing `validation.*`, `concurrency.conflict`,
`resource.not_found`.

## 6. Dev seed (DL-074)

Demo tenant: definition `discount-approval` ("Discount approval", subject `finance` / `discount`), version 1 **published**, one step
"Branch manager approval" (`workflow.tasks.approve`, 48 h SLA, return allowed), `requireDistinctApprovers = true`. Idempotent.
Finance can switch the step to `finance.approve` with a new version once its permission exists.

## 7. How Finance (EEMP-014) should integrate later
1. Reference `EEMP.Modules.Workflow.Contracts` from Finance.Application (contracts only — architecture tests enforce it).
2. On discount/refund request: save it `pending` → `IWorkflowService.StartAsync(new("discount-approval", "finance", "discount",
   discount.Id, invoice.BranchId, "Discount INV-…", $"discount:{discount.Id}"))`; store the returned instance id on the discount.
3. Register `IWorkflowEventHandler` with `SubjectModule = "finance"`; on `completed/approved` apply the discount (guarded update on
   status + instance id, idempotent by `EventId`), on `rejected/cancelled` mark it rejected, on `returned` mark it editable.
4. Replace the local `POST /finance/discounts/{id}/approve|reject` with the workflow inbox actions (or keep them as thin wrappers that
   call the workflow endpoints' service), and `GET /finance/approvals` with `GET /workflow/inbox?definitionKey=discount-approval`.
5. Publish a version 2 of `discount-approval` (and `refund-approval`) whose steps require `finance.approve`; maker-checker is then
   enforced by the engine. Keep `finance.*` audit rows in the finance transaction as today.
