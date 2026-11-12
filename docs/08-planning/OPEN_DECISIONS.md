# Open Decisions — Must Be Resolved

| ID | Decision | Why it matters | Proposed options / notes |
|---|---|---|---|
| OD-001 | Initial target market(s) and legal jurisdictions | Privacy, minors, tax, payments, SMS | Iran-first, EU-first, multi-region, other |
| OD-002 | SaaS tenant definition | Ownership and isolation | One tenant per legal institute group vs organization |
| OD-003 | Central office model | Hierarchy and delegated administration | One central office per tenant; allow multiple? |
| OD-004 | Student cross-institute identity | Duplicate and transfer model | Global person identity with tenant-scoped profiles vs isolated identities |
| OD-005 | Transfer semantics | Legal and operational impact | Referral copy, permanent transfer, or both |
| OD-006 | Consent/guardian rules | Minors and legal authority | Jurisdiction-specific policy |
| OD-007 | Academic grading model | Report correctness | Percentage, letter, numeric, competency, configurable schemes |
| OD-008 | Tuition calculation | Revenue and invoice correctness | Per term, course, credit, installment, custom rules |
| OD-009 | Payment marketplace model | Funds flow and compliance | Institute-owned gateway accounts vs platform collection/split settlement |
| OD-010 | SaaS pricing model | Commercial architecture | Per active student, branch, seat, usage, tier, hybrid |
| OD-011 | Supported currencies and tax | Financial correctness | Single currency initially vs multi-currency |
| OD-012 | Payment providers | Integration scope | Country-specific gateways; provider shortlist required |
| OD-013 | SMS provider(s) and sender rules | Delivery and legal compliance | Platform-managed or tenant-configured |
| OD-014 | Reminder policy precedence | User consent and tenant control | Platform limits → tenant → branch → user preference |
| OD-015 | Online meeting provider | API, cost, recording/privacy | External provider vs self-hosted solution |
| OD-016 | Recorded video hosting | Cost and content protection | S3 + CDN, specialist video platform, hybrid |
| OD-017 | Counseling data classification | Sensitive records | Separate restricted notes and retention |
| OD-018 | Digital card verification | Fraud prevention | QR signed token, online verification, expiry |
| OD-019 | Initial languages | Translation effort | Persian, English, Arabic, Turkish proposed |
| OD-020 | Calendar systems | Locale support | Gregorian plus Jalali/Hijri display needs |
| OD-021 | Custom domains/white label | SaaS routing and certificates | MVP or later phase |
| OD-022 | Data residency and backup region | Contract/regulatory | Per market/tenant requirements |
| OD-023 | Identity provider | SSO/MFA and operations | ASP.NET Identity, external OIDC, hybrid |
| OD-024 | Deployment topology | Cost and scale | Shared cluster, regional cells, dedicated tenant |
| OD-025 | Import/export formats | Onboarding and exit | CSV/XLSX, standard formats, APIs |
| OD-026 | Attendance and timetable scope | Product breadth | Include in MVP or subsequent release |
| OD-027 | Workflow configurability depth | Complexity and safety | Form/state/approval only vs conditions/parallel approvals |
| OD-028 | Subscription suspension behavior | Customer experience and data access | Read-only grace period, restricted mode, immediate suspend |
| OD-029 | Support access approval | Insider risk | Tenant approval, dual control, emergency break-glass |
| OD-030 | RPO/RTO and service tiers | Operational cost | Define per plan and criticality |

Do not proceed with dependent implementation until relevant decisions are resolved or explicitly accepted as assumptions.
