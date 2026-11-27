# EEMP-012 — Consultation and support booking contract

Status: implemented on branch `feature/m4-consultation` (from `main` after PRs #1–#9). Decisions **DL-090..DL-095** (range
DL-090..DL-099 reserved for this task; the parallel Finance completion work uses its own range). Everything in the M1/M2/M3 and
workflow contracts still holds; this document only adds to it. Requirements: CNS-001..CNS-006 (P0/P1), CAL-001 (partial: week view),
`docs/03-domain/CONSULTATION_AND_PLANNING.md`, security baseline #1 (no implicit TenantAdmin access to consultation records), OD-017.

## 1. Scope (DL-090)

In scope: consultant profiles, availability slots per branch (single and weekly bulk), portal booking by students/guardians,
booking lifecycle `requested → confirmed → completed | noShow`, cancellation (`cancelled`) with a configurable cancellation window,
confidential consultation notes, support tickets (portal → staff) with status, assignee and a message thread.

Out of scope (documented gaps): paid sessions (→ EEMP-013, see DL-095), online meeting links / calendar sync (EEMP-016), SMS/email
reminders and notifications (EEMP-017), reschedule as a single operation (portal: cancel + book again), group sessions (capacity > 1),
waiting lists, service catalogue / buffers between slots, attachments on tickets, SLA on tickets, retention/purge jobs for notes
(OD-017 retention period still open), exports.

One backend module **`Consultation`** (`EEMP.Modules.Consultation.*`, schema `consultation`, own migrations history) owns both
consultation bookings and support tickets (MODULE_BOUNDARIES lists "Consultation: availability, bookings, case metadata"; support
tickets are the support half of CNS-002 and share the portal/staff split). Splitting Support into its own module later is mechanical
(separate tables, no shared aggregates).

## 2. Model

All tables tenant-owned (central query filter, tenant-leading indexes, tenant-composite FKs inside the module); branches, users and
students are referenced by id only. All instants UTC (`...At`), serialized with `Z`.

| Table | Fields (besides Id, TenantId, RowVersion, CreatedAt/UpdatedAt) |
|---|---|
| `Settings` | one row per tenant (lazy-created with defaults): `cancellationWindowHours` (24; 0..336), `bookingHorizonDays` (30; 1..180), `minLeadHours` (2; 0..168), `maxActiveBookingsPerStudent` (3; 1..20) |
| `Consultants` | `userId` (unique per tenant), `displayName` (1..200), `bio` (≤1000), `specialties` (≤500), `isActive`; child `ConsultantBranches(consultantId, branchId)` = branches served |
| `Slots` | `consultantId`, `branchId`, `startsAt`, `endsAt` (15..240 min, same consultant may not overlap non-cancelled slots), `mode` `inPerson|online`, `location` (≤200), `status` `open|cancelled` |
| `Bookings` | `slotId`, `consultantId`, `branchId`, `studentId`, `bookedByUserId`, `startsAt`, `endsAt`, `mode`, `location` (copied from the slot), `topic` (1..200), `requesterNote` (≤1000, visible to the family and staff — **not** confidential), `status`, `confirmedAt`, `completedAt`, `cancelledAt`, `cancelledByUserId`, `cancelledBy` `requester|staff`, `cancelReason` (≤500). **Unique filtered index `(TenantId, SlotId) WHERE Status <> 'Cancelled'`** |
| `BookingNotes` | `bookingId`, `authorUserId`, `body` (1..4000), `createdAt`. Append-only (no edit/delete API). Separate table from scheduling metadata (domain doc "Case privacy") |
| `Tickets` | `number` (per-tenant sequence, unique), `branchId`, `studentId`, `createdByUserId`, `subject` (1..200), `category` `general|academic|finance|technical|consultation`, `priority` `low|normal|high`, `status` `open|inProgress|waitingOnRequester|resolved|closed`, `assigneeUserId?`, `lastMessageAt`, `closedAt?` |
| `TicketMessages` | `ticketId`, `authorUserId`, `fromStaff`, `body` (1..4000), `isInternal` (staff-only note), `createdAt` |

### Booking state machine (DL-092)

| From | Action | Who | To |
|---|---|---|---|
| — | book | portal: student for self / guardian for a linked student | `requested` |
| `requested` | `confirm` | booking's consultant; `consultation.bookings.manage` in the branch | `confirmed` |
| `requested`, `confirmed` | `cancel` (reason optional) | portal user who can see the booking, only while `startsAt - now ≥ cancellationWindowHours` (`422 consultation.cancellation_window_passed`); consultant / `bookings.manage`: any time before completion | `cancelled` |
| `confirmed` | `complete` | consultant / `bookings.manage`, only once `now ≥ startsAt` (`422 consultation.session_not_started`) | `completed` |
| `confirmed` | `no-show` | consultant / `bookings.manage`, only once `now ≥ startsAt` | `noShow` |
| terminal (`completed`, `noShow`, `cancelled`) | any | — | `409 consultation.invalid_transition` |

Every action takes the booking `rowVersion` (stale → `409 concurrency.conflict`). A cancelled booking frees the slot (the unique
index ignores cancelled rows) so it can be booked again.

### Booking rules (DL-092)
- **Atomic reservation (CNS-003)**: the slot is reserved by inserting the booking; the unique filtered index makes a second active
  booking for the same slot fail in the database. The service maps that failure to `409 consultation.slot_unavailable`. UI availability
  is advisory only.
- Bookable slot: `status = open`, consultant active and serving the branch, no active booking, `startsAt ≥ now + minLeadHours`
  and `startsAt ≤ now + bookingHorizonDays` (`422 consultation.outside_booking_window`).
- **Branch**: a portal booking is only possible in the **student's own branch** (`422 consultation.branch_not_served` otherwise; the
  portal lists only that branch's consultants/slots).
- **Relationship = authorization**: a guardian may book only for students linked to their guardian profile; a student only for
  themself. Any other `studentId` → `404 resource.not_found` (existence is not revealed).
- A student may hold at most `maxActiveBookingsPerStudent` bookings in `requested`/`confirmed` with `startsAt > now`
  (`422 consultation.booking_limit_reached`).

## 3. Permissions and roles (DL-091)

| Code | Sensitive | Branch-scoped | Use |
|---|---|---|---|
| `consultation.consultants.manage` | false | true | create/edit consultant profiles serving the branch; manage any consultant's slots in the branch; tenant-wide grant also edits settings |
| `consultation.consult` | true | true | act as a consultant in the branch (requires an **active consultant profile** linked to the user): own slots, own bookings, write notes on own bookings |
| `consultation.bookings.read` | true | true | list/read booking metadata (no notes) in the branch |
| `consultation.bookings.manage` | true | true | confirm/cancel/complete/no-show any booking in the branch |
| `consultation.notes.read` | true | true | read confidential notes of any booking in the branch (supervision). Granted to **no built-in role** |
| `support.tickets.read` | true | true | staff inbox and ticket threads (incl. internal notes) in the branch |
| `support.tickets.manage` | true | true | reply, internal notes, status, priority, assignment in the branch |

Built-in roles (synced into existing tenants at startup by the Identity system-role sync, DL-064):
- **TenantAdmin**: no consultation/support code (security baseline #1).
- **BranchManager**: `consultation.consultants.manage`, `consultation.bookings.read`, `consultation.bookings.manage`,
  `support.tickets.read`, `support.tickets.manage` — **not** `consultation.notes.read`.
- **Consultant** (new): `organization.read`, `branches.read`, `consultation.consult`.
- **SupportDesk** (new): `organization.read`, `branches.read`, `support.tickets.read`, `support.tickets.manage`.
- Guardian / Student: none (portal endpoints authorize by relationship).

Consultant identity: a consultant is a **user account** (any staff user) with a `Consultants` profile. The profile alone grants
nothing; the user also needs `consultation.consult` in the branch (Consultant role assignment), so revoking the role or deactivating
the profile both stop access immediately.

## 4. Confidential notes (DL-093, resolves OD-017 for this task)
- Notes are a separate table and a separate endpoint; booking DTOs never contain notes. `canReadNotes`/`canWriteNotes` flags tell the UI.
- **Read**: the booking's own consultant (with `consultation.consult` in the branch), or a holder of `consultation.notes.read` in the
  booking's branch. Everyone else who can see the booking (e.g. BranchManager via `bookings.read`) gets `403 consultation.notes_forbidden`;
  callers who cannot see the booking get `404`. TenantAdmin has neither.
- **Write**: only the booking's consultant, on a booking that is `confirmed` or `completed` (`403 consultation.not_booking_consultant`,
  `409 consultation.invalid_transition`).
- Portal (student/guardian) has **no** notes endpoint and portal DTOs carry no notes.
- Every notes read and write is written to the audit trail (`consultation.notes.read` / entity audit), ids only, never the text.
- Notes are never logged; retention/purge and export restrictions are deferred until OD-017's retention period is decided.

## 5. Support tickets (DL-094)
- Created only from the portal by a student/guardian **about a linked student** (`studentId` required; the ticket's branch is the
  student's branch, derived server-side). The requester sees only tickets they created; internal messages are never returned to the portal.
- Staff with `support.tickets.read` in the branch see the inbox and threads; `support.tickets.manage` replies, writes internal notes,
  changes status/priority and assigns. Assignee must be an existing user in the tenant (`422 support.unknown_assignee`); the UI offers
  "assign to me"/"unassign".
- Status automation: staff public reply → `waitingOnRequester`; requester reply → `open` (also reopens `resolved`); requester may close
  (`closed` is terminal: no further messages, `409 support.ticket_closed`). Staff may set any status except back from `closed`.
- Ticket number: per-tenant sequence (`max + 1`, unique index; a concurrent duplicate is retried).

## 6. HTTP API (`/api/v1`, camelCase JSON, problem+json errors)

Staff / consultant:
- `GET /consultation/settings` (tenant user) → `{ cancellationWindowHours, bookingHorizonDays, minLeadHours, maxActiveBookingsPerStudent, rowVersion }`
- `PUT /consultation/settings` (`consultation.consultants.manage` tenant-wide) same body.
- `GET /consultation/me` (tenant user) → `{ consultant: consultantDto | null, canConsultBranchIds: [guid], canManageBranchIds?: [guid] | "all" }` — lets the UI show consultant screens.
- `GET /consultation/consultants?branchId&includeInactive` (`consultants.manage` or `bookings.read`, filtered to the caller's branch scope; a consultant always sees their own profile) → `[consultantDto]`
- `POST /consultation/consultants` (`consultants.manage` in **every** listed branch) `{ userId, displayName, bio?, specialties?, branchIds: [guid] (1..20) }` → 201 consultantDto. `409 consultation.consultant_exists`, `422 consultation.user_not_found`, `422 consultation.branch_not_found`.
- `GET /consultation/consultants/{id}`; `PUT /consultation/consultants/{id}` `{ displayName, bio?, specialties?, branchIds, isActive, rowVersion }` (manage in every old and new branch).
- consultantDto: `{ id, userId, displayName, bio?, specialties?, isActive, branchIds: [guid], rowVersion }`
- `GET /consultation/slots?from&to&consultantId&branchId` (`from`/`to` ISO instants, span ≤ 62 days; default this week) → `[slotDto]`;
  visible: own slots (consultant), or slots in branches where the caller has `consultants.manage` / `bookings.read`.
- `POST /consultation/slots` `{ consultantId, branchId, startsAt, endsAt, mode, location? }` → 201 slotDto. Allowed for the consultant themself (with `consult` in that branch) or `consultants.manage` in the branch. `409 consultation.slot_overlap`, `422 consultation.slot_in_past`, `422 consultation.branch_not_served`, `422 consultation.consultant_inactive`.
- `POST /consultation/slots/bulk` `{ consultantId, branchId, fromDate, toDate (yyyy-MM-dd, ≤ 62 days), weekdays: ["saturday"..], startTime, endTime ("HH:mm"), durationMinutes (15..240), mode, location? }` → `{ created, skipped, slots: [slotDto] }`. Times are interpreted in the tenant time zone; past and overlapping slots are skipped, not errors.
- `DELETE /consultation/slots/{id}` → 204; slot status becomes `cancelled`. `409 consultation.slot_has_bookings` when an active booking exists (cancel the booking first).
- slotDto: `{ id, consultantId, consultantName, branchId, startsAt, endsAt, mode, location?, status, booking?: { id, status, studentName, topic } , rowVersion }` (booking summary only for callers who can see the booking).
- `GET /consultation/bookings?scope=mine|all&from&to&branchId&consultantId&status&page&pageSize` — `mine`: bookings where the caller is the consultant; `all`: `bookings.read` scope.
- `GET /consultation/bookings/{id}` → bookingDto + `allowedActions: ["confirm","cancel","complete","noShow"]`, `canReadNotes`, `canWriteNotes`.
- `POST /consultation/bookings/{id}/confirm|cancel|complete|no-show` `{ rowVersion, reason? }` → bookingDto.
- `GET /consultation/bookings/{id}/notes` → `[{ id, authorUserId, authorName, body, createdAt }]`; `POST` `{ body }` → 201 note.
- bookingDto: `{ id, slotId, consultantId, consultantName, branchId, studentId, studentName, bookedByUserId, startsAt, endsAt, mode, location?, topic, requesterNote?, status, confirmedAt?, completedAt?, cancelledAt?, cancelledBy?, cancelReason?, createdAt, rowVersion }`.

Portal (student/guardian; relationship-authorized):
- `GET /portal/consultation/consultants?studentId` → `[{ id, displayName, bio?, specialties?, nextSlotAt? }]` (student's branch).
- `GET /portal/consultation/slots?studentId&consultantId?&from?&to?` → `[{ id, consultantId, consultantName, branchId, startsAt, endsAt, mode, location? }]` bookable only.
- `POST /portal/consultation/bookings` `{ studentId, slotId, topic, note? }` → 201 portalBookingDto.
- `GET /portal/consultation/bookings?studentId?` → `[portalBookingDto]` (newest first; all linked students when omitted).
- `POST /portal/consultation/bookings/{id}/cancel` `{ rowVersion, reason? }` → portalBookingDto.
- portalBookingDto: `{ id, studentId, studentName, consultantName, branchId, startsAt, endsAt, mode, location?, topic, requesterNote?, status, cancelledBy?, cancelReason?, canCancel, cancelDeadline, rowVersion }` — no notes, no consultant user ids.
- `GET /portal/support/tickets` → `[ticketSummary]`; `POST /portal/support/tickets` `{ studentId, subject, category, body }` → 201 ticketDetail.
- `GET /portal/support/tickets/{id}` → ticketDetail (public messages only); `POST /portal/support/tickets/{id}/messages` `{ body }`; `POST /portal/support/tickets/{id}/close` `{ rowVersion }`.

Support staff:
- `GET /support/tickets?status&branchId&assignee=me|unassigned|any&page&pageSize` (`support.tickets.read` scope) → paged `[ticketSummary]`, newest activity first.
- `GET /support/tickets/{id}` → ticketDetail incl. internal messages.
- `POST /support/tickets/{id}/messages` (`manage`) `{ body, isInternal }`; `PUT /support/tickets/{id}` (`manage`) `{ status, priority, assigneeUserId?, rowVersion }`.
- ticketSummary: `{ id, number, branchId, studentId, studentName, subject, category, priority, status, assigneeUserId?, assigneeName?, createdAt, lastMessageAt, rowVersion }`; ticketDetail adds `messages: [{ id, authorName, fromStaff, isInternal, body, createdAt, mine }]`.

## 7. Error codes (localized fa/en/tr/ar in `messages.*.json`)
`consultation.consultant_exists` 409 · `consultation.user_not_found` 422 · `consultation.consultant_inactive` 422 · `consultation.branch_not_found` 422 ·
`consultation.branch_not_served` 422 · `consultation.slot_overlap` 409 · `consultation.slot_in_past` 422 ·
`consultation.slot_unavailable` 409 · `consultation.slot_has_bookings` 409 · `consultation.outside_booking_window` 422 ·
`consultation.booking_limit_reached` 422 · `consultation.cancellation_window_passed` 422 · `consultation.invalid_transition` 409 ·
`consultation.session_not_started` 422 · `consultation.notes_forbidden` 403 · `consultation.not_booking_consultant` 403 ·
`support.ticket_closed` 409 · `support.unknown_assignee` 422. Plus the generic `validation.*`, `resource.not_found`,
`authorization.forbidden(_scope)`, `concurrency.conflict`.

## 8. Workflow engine, finance, calendar (DL-095)
- **Workflow engine (EEMP-011) is not used** for bookings or tickets: both are single-actor state machines with no approval chain,
  and putting them through approvals would add latency without a control. The module keeps its own transition tables and history via
  the entity audit trail. EEMP-013 (academic planning) is where the engine is expected (planning request approval).
- **Paid sessions deferred** to EEMP-013: Finance exposes no in-process "create and issue an invoice for an arbitrary service" contract
  (invoices are generated per enrollment × fee item) and Finance is being completed on a parallel branch; adding one now would edit
  finance files. Slots/bookings carry no price; "payment requirement" (domain doc) is a documented gap.
- No meeting links/calendar sync (EEMP-016), no reminders (EEMP-017). `location` is free text; for `online` mode the UI says the link
  will be shared by the consultant.

## 9. Dev seed
Demo tenant (skipped when any consultant exists): user `consultant.one` (role Consultant, North branch, password = demo user password),
consultant profile "Dr. Narges Tavakoli" serving North; open slots on the next 5 working days (Sat–Wed) 10:00–12:00 Tehran in 30-min
steps, in person, room "Counseling 1"; one `requested` booking for Sara (S0001, guardian.one) on the first slot; one open support
ticket from guardian.one about Sara.
