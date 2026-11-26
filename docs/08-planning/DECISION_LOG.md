# Decision Log

## Approved carried-forward decisions
- DL-037..DL-042: previously approved EEMP M1 security/tenancy decisions. Preserve their substance as summarized in `docs/04-security/APPROVED_SECURITY_BASELINE.md`: tenant admins do not receive implicit access to student/guardian/teacher/consultation/finance records; sibling institute isolation; supervision grants max 90 days with fresh renewal approval; small-cell suppression under 5 where applicable; revocation effective on next authorization; support access decisions audited.

## Approved decisions (2026-11-12)

### DL-043 — Architecture baseline
- Date: 2026-11-12
- Status: Approved
- Context: Analysis in `docs/10-analysis/` (CF-01, D-01, D-02). Approval given by the product owner in the working session as "Approve as proposed".
- Decision: Modular Monolith + Clean Architecture; API-first ASP.NET Core Web API + Next.js/TypeScript; transactional outbox/inbox for external side effects; one schema and one DbContext per module, cross-module references by ID only, and architecture tests that fail the build on forbidden module/layer references.
- Alternatives: Microservices (rejected as premature); unconstrained shared schema (rejected, boundaries not enforceable).
- Consequences: AGENTS.md "approved modular boundaries" is now accurate. The canonical module list in `docs/10-analysis/03_ARCHITECTURE_AND_DATA_MODEL_REVIEW.md` §3 is **not** yet approved; CF-05 remains open until it is.
- Owner: Product owner.

### DL-044 — Version control
- Date: 2026-11-12
- Status: Approved
- Context: Repository was not under version control (A-01); completion evidence requires commits.
- Decision: Initialize a git repository at the project root. First commit contains the existing documentation only, no application code.
- Alternatives: Owner-initialized repository (declined).
- Consequences: Branch and pull-request policy is still to be defined in EEMP-002. Commits need a configured author identity.
- Owner: Product owner.

### DL-045 — Database engine
- Date: 2026-11-12
- Status: Approved
- Context: OD-024 / D-11. Product owner selected SQL Server as proposed.
- Decision: SQL Server with EF Core is the target engine for EEMP-002/003. Hosting, edition and licensing remain to be confirmed (TECHNOLOGY_STACK selection gates). Double-booking and overlap protection uses slot-row uniqueness / application locks (D-05), because SQL Server has no range-exclusion constraint.
- Alternatives: PostgreSQL (not selected).
- Consequences: SQL Server RLS is an intended defense-in-depth layer but is not relied on until the EEMP-003 harness proves pooled-connection context handling (T-09); if it cannot be proven, record that in this log.
- Owner: Product owner.

### DL-046 — Authorized scope
- Date: 2026-11-12
- Status: Approved
- Decision: Only EEMP-002 (repository, CI, local environment) is authorized. EEMP-003..005 remain blocked on OD-002, OD-004, OD-023, OD-029 and Q-1..Q-5. Stop for user verification after EEMP-002.
- Owner: Product owner.

### DL-047 — Backend runtime version
- Date: 2026-11-12
- Status: Approved (selected within the authorized EEMP-002 scope; product owner may amend)
- Context: TECHNOLOGY_STACK.md says to use a supported LTS selected at implementation start. Microsoft release index checked on 2026-11-12: 10.0 is LTS, active, latest SDK 10.0.404, EOL 2028-11-14; 8.0 and 9.0 reached EOL on 2026-11-10; 11.0 is STS.
- Decision: .NET 10 LTS. SDK pinned in `global.json`; installed locally at `~/.dotnet` (user-level, 10.0.404, runtime 10.0.15) via Microsoft's official `dotnet-install.sh`.
- Alternatives: .NET 8 LTS and 9 STS (end of life); .NET 11 STS (short support).
- Consequences: Package versions are managed centrally and must be re-reviewed against advisories at each upgrade.
- Owner: Implementing agent, pending product-owner confirmation.

## Milestone 1 decisions (2026-11-24)

### DL-048 — Milestone 1 scope authorization
- Date: 2026-11-24
- Status: Approved (explicit product-owner instruction in the working session)
- Context: DL-046 authorized only EEMP-002. The product owner then instructed implementation of milestone 1 = EEMP-002..007, backend and frontend in parallel against a shared contract (`/api/v1`).
- Decision: Implement EEMP-002..007 on branch `feature/m1-backend` (backend) and open a draft PR to `main`. Items move to `IN_REVIEW`, not `DONE`, until user verification.
- Consequences: EEMP-000/001 remain formally open; the open decisions M1 depends on are covered by the accepted assumptions below.
- Owner: Product owner.

### DL-049 — Accepted assumptions — pending business confirmation
- Date: 2026-11-24
- Status: Accepted assumption — pending business confirmation (each item can be revisited; changing it later has the noted cost)
- Context: Open decisions blocking EEMP-003..007 (DL-046). The product owner directed that these working assumptions be used for M1.
- Decision:
  - OD-001: Iran-first market and jurisdiction.
  - OD-019: locales `fa-IR` (default, RTL), `en` (LTR), `tr` (LTR), `ar` (RTL). Server-generated text (problem titles) is localized from catalogs for fa/en/tr/ar; clients translate stable `code`s.
  - OD-020: Jalali display for fa, Gregorian for en/tr, Gregorian (optionally Hijri later) for ar. All instants persisted in UTC; dates of birth as `date`.
  - OD-002: one tenant = one institute group (organization).
  - OD-003: exactly one central office per tenant (unique index), many branches.
  - OD-004: identities isolated per tenant for MVP (user name unique per tenant; the same person in two tenants has two accounts).
  - OD-011: single currency per tenant (default IRR); money is `decimal` with explicit currency.
  - OD-023: ASP.NET Core Identity (password hashing, lockout) + JWT access token (15 min) + rotating refresh token stored hashed in the database.
  - OD-024: single shared deployment (one database, schema per module, TenantId on tenant-owned rows), Docker.
- Alternatives: see `OPEN_DECISIONS.md` for each item.
- Consequences: changing OD-004 to a global person identity needs a person-identity table and a migration of user/profile links; changing OD-003 needs dropping the unique central-office index; changing OD-011 needs currency on every money column (no money columns exist yet).
- Owner: Product owner (confirmation pending).

### DL-050 — Documentation conflict: browser auth (BFF cookies) vs JWT
- Date: 2026-11-24
- Status: Accepted assumption — pending business confirmation
- Context: `docs/10-analysis/03_ARCHITECTURE_AND_DATA_MODEL_REVIEW.md` D-04 and `06_IMPLEMENTATION_SPEC_PHASE1.md` §4 recommend a Next.js BFF with HttpOnly/Secure/SameSite cookies and CSRF protection, no tokens in `localStorage`; the M1 shared contract (OD-023 assumption) specifies JWT access + refresh tokens returned in the login response.
- Decision: Both, layered. The API issues bearer JWT + refresh tokens (contract). Browsers never hold them in script-readable storage: the Next.js apps act as BFF, keep tokens in HttpOnly cookies and enforce CSRF on mutating requests (frontend scope). The API has no cookie authentication, so it has no CSRF surface itself.
- Alternatives: cookie authentication directly on the API (would need CSRF on the API and same-site deployment); tokens in browser storage (rejected by D-04).
- Consequences: the "CSRF test passes for cookie auth" acceptance item belongs to the frontend BFF; the API's refresh endpoint must only be reachable by the BFF in production (network/CORS policy to be defined in deployment).
- Owner: Product owner.

### DL-051 — Documentation conflict: global identity vs per-tenant identities
- Date: 2026-11-24
- Status: Accepted assumption — pending business confirmation
- Context: `OPEN_DECISIONS.md` OD-004 lists "global person identity with tenant-scoped profiles" as an option and the cross-institute referral design (`CROSS_INSTITUTE_REFERRAL.md`) assumes identities can be matched across tenants; the M1 contract assumes isolated identities per tenant.
- Decision: Per-tenant isolated identities for M1 (DL-049, OD-004). Platform administrators are the only tenant-less accounts. A user account (Identity) is separate from person profiles (People), linked by `UserId`, so a later global-person layer can be added without merging accounts.
- Consequences: cross-institute referral/transfer (EEMP-019) must re-open OD-004 before implementation.
- Owner: Product owner.

### DL-052 — M1 implementation notes and known gaps
- Date: 2026-11-24
- Status: Recorded (implementing agent)
- Decision / facts:
  - One DbContext and schema per module (`tenancy`, `identity`, `organization`, `people`, `audit`), each with its own `__EFMigrationsHistory` table (DL-043).
  - SQL Server RLS (DL-045 defense-in-depth) is **not** implemented in M1: pooled-connection session-context handling was not proven (no SQL Server in the build sandbox). Isolation relies on EF global query filters (fail closed), save-pipeline tenant checks, tenant-composite foreign keys and service-level scope checks, covered by integration tests.
  - Integration tests run on a SQLite fallback provider (schema from model, rowversion emulated); the SQL Server migrations were generated and checked for pending model changes but not applied to a real SQL Server.
  - Tenant provisioning (tenant, central office, roles, first admin) writes to several module contexts without one transaction; a failure mid-way leaves a partial tenant (logged, 500). Outbox/compensation is future work.
  - Out-of-scope reads return 404 (no existence leak to sibling branches/tenants); out-of-scope writes into a visible scope return 403 `authorization.forbidden_scope`.
  - Redis is provisioned in docker-compose for upcoming work; M1 does not use it (rate limiting is in-process, per instance).
- Owner: Implementing agent.

## Milestone 2 decisions (2026-11-24)

### DL-053 — Milestone 2 scope authorization
- Date: 2026-11-24
- Status: Approved (explicit product-owner instruction in the working session)
- Context: Milestone 2 = academic core, tracker EEMP-008 (academic catalog & class operations), EEMP-009 (enrollment & attendance),
  EEMP-010 (assessment, grades, report cards). Backend built against a shared contract (`eemp-milestone2-contract.md`, `/api/v1`).
- Decision: Implement EEMP-008..010 backend on branch `feature/m2-academic-backend`, stacked on `feature/m1-backend` (PR #1); open a draft PR
  to `main`. Items move to `IN_REVIEW`, not `DONE`, until user verification. M1 items stay `IN_REVIEW`.
- Owner: Product owner.

### DL-054 — Accepted assumptions for OD-007, OD-026 and OD-020 (academic dates) — pending business confirmation
- Date: 2026-11-24
- Status: Accepted assumption — pending business confirmation
- Context: OD-007 (grading model) and OD-026 (attendance/timetable scope) block EEMP-009/010; OD-020 needs a rule for date-only academic fields.
- Decision:
  - OD-007: configurable grading schemes per tenant. Built-in schemes created per tenant on first use: `IR20` numeric 0–20, pass 10,
    2 decimals (**default**, Iranian scale); `PCT` percentage 0–100, pass 50; `LETTER` A–F bands on 0–100, pass 60. Tenants may add/edit
    schemes (min/max/pass, decimals, bands, whether missing/absent counts as zero). A class fixes its scheme at creation (class → course
    → tenant default) and it cannot change once assessments exist. Formula `weightedAverage.v1`: weighted mean of score/maxScore, exempt
    excluded, absent/missing = 0 unless the scheme says otherwise, scaled to the scheme range, rounded half away from zero; band = highest
    `minValue ≤ total`; pass = total ≥ passScore. Competency-based grading, moderation and per-term/course overrides beyond the class
    scheme are not included.
  - OD-026: attendance **and** a weekly timetable are in the MVP. Timetable = recurring weekly slots per class (weekday, local start/end
    time, room). Attendance = one sheet per class, date and optional slot, statuses present/absent/late/excused, bulk idempotent upsert,
    no future dates, date inside the class's term. Holiday calendars, one-off schedule exceptions, substitutions-by-date and attendance
    policies (thresholds, notifications) are out of scope.
  - OD-020 (academic dates): date-only fields are ISO Gregorian `yyyy-MM-dd` in the API and the database (`date`), meaning tenant-local
    calendar dates; local times are `HH:mm` wall-clock in the tenant time zone; clients convert to/from Jalali for `fa`. "Today" for
    rules is computed in the central office time zone (default `Asia/Tehran`). Week display starts on Saturday.
- Alternatives: see `OPEN_DECISIONS.md` (percentage/letter/competency only; attendance without timetable; Jalali strings in the API).
- Consequences: changing the default scale is configuration only. A new formula needs a new formula id (report-card snapshots record
  `weightedAverage.v1`). Adding holidays/exceptions later adds tables without changing existing ones.
- Owner: Product owner (confirmation pending).

### DL-055 — Milestone 2 implementation decisions
- Date: 2026-11-24
- Status: Recorded (implementing agent) — items marked *needs confirmation* await the product owner
- Decision / facts:
  - **Module split (needs confirmation):** one `Academic` module (schema `academic`, own `__EFMigrationsHistory`) holds catalog, classes,
    enrollment, attendance, assessment and report cards. `MODULE_BOUNDARIES.md` lists Academic Catalog, Enrollment, Assessment and Report
    Cards as separate modules, but the canonical module list is not approved (DL-043, CF-05) and the work order allowed either option.
    One module keeps capacity/enrollment/attendance/grade rules transactional; the internal services are already separated so a later
    split needs contracts but no schema redesign. Cross-module access is via contracts only: `IPeopleDirectory` (People),
    `IOrganizationSettings` (Organization), `IAcademicUsage` (Academic, used by branch/student/teacher deletion guards).
  - **Permissions:** 15 new codes (`catalog.*`, `classes.*`, `enrollments.*`, `attendance.*`, `grades.read|manage|approve|publish`,
    `reportcards.read|manage|publish`). The M1 `<resource>.read|manage` convention is extended with `approve` and `publish` so score
    entry, approval and publication can be held by different people (segregation of duties). Student-data codes are sensitive and
    branch-scoped; catalog codes are tenant-wide and not sensitive.
  - **Default roles:** TenantAdmin gets `catalog.*` and `classes.*` only — no enrollment/attendance/grade/report-card access (security
    baseline #1). BranchManager gets all academic codes. Teacher/Guardian/Student roles get none.
  - **Teachers act through assignments, not permissions:** an active, effective-dated `TeacherAssignment` on a class (for the teacher
    profile linked to the user) allows reading the class/roster, attendance, assessment definition, score entry and submit — on that
    class only. Approve/return/reopen/publish, enrollment, schedule and report cards always need the permission.
  - **Portal visibility:** students see themselves, guardians see linked students (`StudentGuardian`). Grades come from a published
    snapshot copied at publish time, so reopening a published assessment for correction keeps the last published values visible until
    republished. Report cards are visible only as their latest published version.
  - **Grade audit:** `academic.GradeAuditEntries` is an append-only history of every score change (old/new score and mark, actor,
    reason) and every workflow transition, in addition to the generic audit trail (which records field names only).
  - **Report cards:** one per student × term; content is an immutable JSON snapshot per version (formula id, scheme, lines, average,
    attendance). Regenerating a draft replaces it; regenerating a published card creates a new draft version; publishing supersedes the
    previous published version. Only `approved`/`published` assessments are counted.
  - **Concurrency:** rowversion on every aggregate; class capacity is guarded by updating `EnrolledCount` under the class rowversion;
    score sheets bump the assessment rowversion; bulk attendance is an upsert keyed by a unique (class, date, slot) index with one retry
    on a concurrent-insert race.
  - Deleting a branch referenced by classes, or a student/teacher referenced by enrollments/assignments/report cards, is refused
    (`branch.in_use`, `people.in_use`).
- Owner: Implementing agent.

### DL-056 — Milestone 2 known gaps
- Date: 2026-11-24
- Status: Recorded (implementing agent)
- Facts:
  - Report-card PDF generation, templates per academic level/locale and printable output are not implemented (JSON snapshot only).
    *Update 2026-11-26:* server-side PDF export added (DL-057, DL-058); per-level/per-tenant templates remain open.
  - System roles of tenants created before M2 are not updated with the new permissions (provisioning only creates missing roles);
    existing tenants must have roles edited or a role-sync step added. The demo tenant seed is created fresh in tests/dev.
  - Teacher substitutions by date, holidays and timetable exceptions are not modeled (DL-054).
  - Same SQLite/SQL Server limitations as M1 (DL-052): the `academic` migration was generated and checked, not applied to SQL Server.
- Owner: Implementing agent.

### DL-057 — PDF rendering library and font for report cards
- Date: 2026-11-26
- Status: Recorded (implementing agent) — library choice reversible; QuestPDF can be adopted later if the business confirms eligibility or buys a licence
- Context: report cards must be rendered on the server as PDF in fa/ar (RTL, joined letters, Persian/Arabic-Indic digits) and en/tr.
  The suggested library was QuestPDF. Its licence was checked on 2026-11-26 (https://www.questpdf.com/license/community.html and
  https://www.questpdf.com/license/guide.html, Community License v3.0, effective 6 July 2026): it is a *source-available commercial*
  licence, not OSI. It is free only for individuals, small businesses with annual gross revenue under USD 1,000,000 (consolidated),
  qualifying charities/academic institutions, and OSI-licensed open-source projects; public-sector entities and publicly traded companies
  are never eligible; production use otherwise needs a paid Professional/Enterprise licence (90-day transition when eligibility is lost).
  Code written by an AI agent for an organisation counts as that organisation's developer use.
- Decision: **QuestPDF is not used.** EEMP is a commercial multi-tenant SaaS whose operator revenue, ownership and customer mix (possibly
  public schools) are not confirmed, and the repository has no OSI licence, so Community eligibility cannot be asserted and a later paid
  licence would be a recurring cost decided by the business, not by engineering. Instead the backend renders PDFs with
  **SkiaSharp 4.157.0 (MIT)** (Skia PDF backend, `SKDocument.CreatePdf`) and **HarfBuzzSharp 14.4.0.200 (MIT)** for OpenType shaping
  (Arabic joining, ligatures, marks, `locl` for Persian digits), plus a small in-house layer (`EEMP.Infrastructure/Pdf`): a minimal
  Unicode bidi (UBA W1–W7, N1–N2, L2 without explicit embeddings; paragraph direction from the first strong character, like
  `dir="auto"`), word wrap, and a flow layout with tables (repeated headers), mirrored for RTL, two-pass for "page n of N". Text runs are
  written with their UTF-8 text and clusters, so the PDF text is extractable/searchable (verified with poppler `pdftotext`).
  Native assets: `SkiaSharp.NativeAssets.Linux.NoDependencies` and `HarfBuzzSharp.NativeAssets.Linux` (glibc + libstdc++ only; no
  fontconfig, no system fonts).
- Font: **Vazirmatn v33.003** Regular and Bold (SIL Open Font License 1.1) embedded as resources in `EEMP.Infrastructure`
  (`backend/src/EEMP.Infrastructure/Pdf/Fonts/`, licence text `OFL.txt` committed next to the fonts and copied to
  `licenses/Vazirmatn-OFL.txt` in the build output). Vazirmatn covers Persian, Arabic and Latin incl. Turkish letters, so one family
  serves all four languages. The OFL allows embedding/bundling; the font is not sold on its own and keeps its name.
- Alternatives: QuestPDF (licence above); iText 7/8 (AGPL or commercial); PdfSharp/MigraDoc (MIT, no complex-script shaping);
  headless Chromium HTML→PDF (best shaping, but a browser in the API image and a larger attack surface).
- Consequences: we own a small text-layout layer (bidi is a pragmatic subset; explicit embeddings/isolates are not supported — data
  strings are rendered with first-strong direction). Fonts are embedded as Skia writes them (~150 KB per report card; whether
  the SkiaSharp native build subsets them was not verified). Swapping to QuestPDF later only replaces `ReportCardPdfRenderer` and the Pdf layer; endpoints and tests stay.
- Owner: Implementing agent (licence/business confirmation: product owner).

### DL-058 — Report-card PDF export behaviour
- Date: 2026-11-26
- Status: Recorded (implementing agent)
- Decision / facts:
  - The PDF is built only from the stored `ReportCardVersion.Content` snapshot (student, year, term, lines, scores, totals, band labels,
    average, attendance) — never from client data and never recomputed. Institute (central office name) and branch names are display
    labels read at render time via `IOrganizationSettings.GetInstituteNameAsync` / `IBranchDirectory.GetNameAsync` (they are not part of
    the snapshot; a later rename shows the new name on re-download).
  - Endpoints (contract §8): admin `GET /report-cards/{id}/versions/{versionNo}/pdf` (`reportcards.read`, card's branch in scope, any
    version status — drafts/superseded are marked with a notice banner); portal `GET /portal/me/report-cards/{id}/pdf` (latest published
    version, own student or linked guardian only; teachers are not portal readers of report cards); admin
    `GET /report-cards/pdf-archive?classId=` (ZIP of the *published* versions of the class's non-withdrawn students in the class's term,
    within the caller's `reportcards.read` scope, at most 200 cards → otherwise `422 academic.archive_too_large`). Everything outside scope
    → 404 (no existence leak), consistent with the JSON reads.
  - Language: `?lang=` (fa-IR, fa, en, tr, ar; region subtags accepted) else the Accept-Language request culture, default fa-IR; other
    values → `400 validation.failed` (`lang: validation.unsupported_locale`). Labels come from the server catalogs
    (`reportCard.pdf.*` in `messages.{fa,en,tr,ar}.json`). Dates: Jalali with Persian digits for fa; Gregorian otherwise (Arabic-Indic
    digits for ar, matching the web apps' `nu-arab`). Instants are shown in the tenant time zone. Snapshot dates are parsed invariantly.
  - Response: `application/pdf` (or `application/zip`), `Content-Disposition: attachment` with a file name made of the card id prefix and
    version only (`report-card-1a2b3c4d-v2.pdf`) — no names or student numbers in headers or logs. ZIP entries are named by the
    institute-assigned student number (sanitised to `[A-Za-z0-9_-]`), e.g. `S0001-v1.pdf`. `Cache-Control: no-store`.
  - The BFF streams `application/pdf|zip|octet-stream` responses through (no buffering), forwards `Content-Disposition`, adds `nosniff`;
    the browser saves the blob via a temporary object URL. HTML print stays available next to the download button.
- Known gaps: PDF downloads are not written to the audit trail (reads are not audited anywhere yet); PDFs are not tagged (PDF/UA) or
  PDF/A; no per-tenant logo/letterhead or per-level templates (EEMP-021); archive is generated synchronously in memory (bounded by the
  200-card cap); the Docker image was not built in the sandbox (no Docker) — native libraries were verified on linux-arm64 (sandbox) and
  are published for linux-x64/arm64 and musl.
- Owner: Implementing agent.

## Milestone 3 decisions (2026-11-26) — institute finance (EEMP-014 backend)

Contract: `work/eemp-milestone3-contract.md` (kept next to the M1/M2 contracts). EEMP-011 (workflow engine) is not built; the product
owner approved (2026-11-26) that discount and refund approvals live inside Finance as a local maker-checker step for now (DL-063).

### DL-059 — Money representation and currency
- Date: 2026-11-26
- Status: Recorded (implementing agent) — resolves OD-011 for M3 as "single currency per tenant"
- Context: AGENTS.md requires decimal money with explicit currency; Tenant.Currency (default IRR) existed but no module could read it.
  IRR has no minor unit, but percentage discounts and VAT produce fractions, and tr/ar tenants may later use currencies with decimals.
- Decision: `decimal(19,4)` columns with a `Currency` (ISO 4217, 3 chars) column on every monetary record (fee item, invoice, payment,
  refund, adjustment). Currency comes only from the tenant (`ITenantFinanceSettings` in Tenancy.Contracts) and is stamped at creation.
  Every stored amount is rounded to the currency's minor unit (IRR/IRT 0 decimals) with MidpointRounding.AwayFromZero; client amounts
  with more decimals are rejected. Installment remainders go to the last installment.
- Alternatives: `decimal(18,0)` IRR-only (simpler, but a schema change for any decimal currency); integer minor units (long).
- Consequences: one currency per tenant in M3; multi-currency would add FX rules, not a schema change.
- Owner: Implementing agent; confirm with product owner (OD-011).

### DL-060 — Gap-free per-tenant document numbering
- Date: 2026-11-26
- Status: Recorded (implementing agent)
- Decision: a `finance.NumberSeries` row per (tenant, series) — `INV`, `R` (receipts), `CN` (credit notes) — with a rowversion.
  A number is taken by incrementing the row **in the same SaveChanges/transaction** as the document that uses it; a concurrency
  conflict rolls everything back and the whole operation is retried (bounded, 5 attempts) on a fresh change tracker. Because the
  number and the document commit together, a failure never consumes a number (gap-free) and two documents never share one (unique
  index on (TenantId, Number) per document type as a second guard). Numbers never reset (no yearly reset in M3); format
  `{series}-{000000}`.
- Alternatives: SQL Server SEQUENCE (fast, but gaps on rollback); MAX()+1 (races).
- Owner: Implementing agent.

### DL-061 — Allocation order and credit balance
- Date: 2026-11-26
- Status: Recorded (implementing agent)
- Decision: a succeeded payment is allocated to the requested invoice, else to the student's issued/partially paid invoices by
  due date (then issue date, number); inside an invoice to open installments by due date. Any remainder stays on the payment as
  `unallocated` = the student's credit balance, usable via apply-credit or refundable (DL-063). Voiding an invoice releases its
  allocations back to the payments' unallocated amounts.
- Owner: Implementing agent.

### DL-062 — VAT per tenant
- Date: 2026-11-26
- Status: Recorded (implementing agent) — rate and taxable fee types must be confirmed by the institute's accountant
- Decision: `FinanceSettings.VatEnabled` (default false) and `VatRatePercent`; VAT applies to lines whose fee type is `taxable`, on the
  line amount after a proportional share of approved discounts, at the rate in force when the invoice is issued (stored on the invoice).
  No jurisdiction engine; no e-invoicing (Iranian tax system "سامانه مودیان") integration in M3.
- Owner: Implementing agent; confirm with product owner.

### DL-063 — Local maker-checker for discounts and refunds; append-only finance audit
- Date: 2026-11-26
- Status: Approved by product owner (local approval step), design recorded by implementing agent
- Decision: discounts/scholarships and refunds are created `pending` by a maker and approved/rejected by a different user holding
  `finance.approve` in the record's branch (`403 finance.maker_checker` for the same user). The approval step is behind a small
  `IFinanceApprovals` seam inside the module so it can later be replaced by the EEMP-011 workflow engine without changing the
  public API. Because the generic audit stores only changed field names, every finance state change also writes an append-only
  `finance.FinanceAudit` row (actor, action, entity, invoice/payment/student ids, amount, currency, UTC time) in the same SaveChanges;
  the context refuses to update or delete audit rows.
- Workflow seam (2026-11-26, after EEMP-011 draft PR #7): `IFinanceApprovals.SubmitAsync(ApprovalSubject)` is called in the maker's unit
  of work and is a no-op locally; a workflow implementation calls `IWorkflowService.StartAsync` with idempotency key `finance:{kind}:{id}`
  and its `IWorkflowEventHandler` (SubjectModule `finance`) applies the outcome through `ApprovalService.DecideDiscountAsync/DecideRefundAsync`,
  which are idempotent for replayed (at-least-once) decisions. `EnsureCanDecideAsync` holds the local maker ≠ checker rule.
- Owner: Product owner (approval model), implementing agent (design).

### DL-064 — FinanceOfficer role and startup system-role sync
- Date: 2026-11-26
- Status: Recorded (implementing agent)
- Decision: new built-in role `FinanceOfficer` (all `finance.*` + organization/branches/catalog/classes read, students/guardians/
  enrollments read). TenantAdmin and BranchManager get no `finance.*` code. System roles were only created at provisioning, so an
  idempotent startup sync creates missing system roles in every tenant and **adds** missing catalog codes to existing system roles
  (never removes codes, never touches custom roles).
- Owner: Implementing agent.

### DL-065 — Unpaid balance blocking academic results
- Date: 2026-11-26
- Status: Recorded (implementing agent) — default off
- Decision: `FinanceSettings.BlockResultsOnOverdue` (default false). When on, portal grade and report-card reads/PDFs of a student with
  an overdue issued balance return `403 finance.results_blocked`; staff views are never blocked. Academic reads it through
  `IFinanceStanding` (Finance.Contracts).
- Owner: Implementing agent; policy to be confirmed by product owner (it is legally/ethically sensitive).

### DL-066 — Payment gateways
- Date: 2026-11-26
- Status: Recorded (implementing agent)
- Decision: `IPaymentGateway` (request/verify) with a `fake` gateway (enabled only by configuration, used in Development/Testing) and a
  Zarinpal v4 adapter (`payment/request.json`, `payment/verify.json`), disabled by default, configured only through configuration
  (`Finance__Gateways__Zarinpal__MerchantId` from the environment; nothing committed). The callback is anonymous, bound to the
  payment id in the URL and the provider authority stored at request time; verification always uses the stored amount; replays and
  concurrent callbacks are idempotent (rowversion + status check); a provider amount that differs from the stored amount fails the
  payment. Gateway refunds and reconciliation/settlement files are out of scope for M3.
- Owner: Implementing agent.

## Proposed, not yet approved
- SQL Server RLS defense-in-depth (engine approved in DL-045; shared database with TenantId adopted as an M1 assumption in DL-049; RLS not implemented, see DL-052).
- Provider adapters for payments, SMS, video, storage.
- Translation-key-based i18n with RTL/LTR (server side adopted for M1 via stable error codes, DL-049).
- Durable notification scheduling with outbox/inbox and idempotency.

## New decisions
Add entries using:
- ID:
- Date:
- Status: Proposed / Approved / Rejected / Superseded
- Context:
- Decision:
- Alternatives:
- Consequences:
- Owner:
