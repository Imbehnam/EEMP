# Domain Model — Conceptual

## Organization
Platform, Tenant, CentralOffice, Institute, Branch, TenantMembership, BranchAssignment, Subscription, Plan, Entitlement, UsageRecord.

## Identity and people
UserAccount, Person, StudentProfile, TeacherProfile, StaffProfile, GuardianRelationship, EmergencyContact, ConsentRecord, Role, Permission, RolePermission, UserRoleAssignment, ScopeAssignment.

## Academic
AcademicYear, Term, Subject, Course, CourseOffering, ClassGroup, Classroom/Location, Timetable, TeacherAssignment, StudentEnrollment, AttendanceSession, AttendanceRecord, Assessment, AssessmentResult, GradingScheme, GradeApproval, ReportCard, ReportCardVersion.

## Workflow
WorkflowDefinition, WorkflowVersion, WorkflowInstance, WorkflowTask, WorkflowTransition, Approval, Comment, AttachmentReference, SLAClock, Delegation.

## Consultation and planning
ServiceCatalog, StaffAvailability, AppointmentSlot, Booking, ConsultationCase, PlanningRequest, PlannerAssignment, StudyPlan, PlanVersion, PlanGoal, PlanTask, FollowUp.

## Finance
FeeItem, TuitionPolicy, Discount, Scholarship, InstallmentPlan, Invoice, InvoiceLine, PaymentIntent, PaymentAttempt, PaymentSettlement, Refund, Chargeback, LedgerEntry, ReconciliationBatch, Receipt.

## Learning and sessions
LearningCourse, Module, Lesson, MediaAsset, CourseEnrollment, ContentEntitlement, LearningProgress, CalendarEvent, RecurrenceRule, OnlineMeeting, MeetingParticipant.

## Communications
NotificationPolicy, NotificationTemplate, NotificationSchedule, DeliveryAttempt, UserNotificationPreference, ProviderConfiguration, WebhookInbox, OutboxMessage.

## Cross-institute sharing
ReferralRequest, ReferralDataScope, ConsentGrant, TransferPackage, TransferAcceptance, ImportMapping, ProvenanceRecord, AccessGrant.

## Cards and documents
CardTemplate, IssuedCard, DocumentTemplate, GeneratedDocument, RevocationRecord.

## Important invariants
- Every tenant-owned record has a verified tenant boundary.
- Branch assignment is explicit and effective-dated where needed.
- Parent access depends on active relationship and permitted authority.
- Teacher access depends on assignment and class/term scope.
- Published report cards are immutable versions; corrections create new versions.
- Financial ledger entries are append-only.
- Workflow instances remain tied to the version they started with.
- Referral data scope is explicit, purpose-bound, and expiring.
- A booking cannot be double-confirmed for the same constrained resource/time.
- Reminder delivery is idempotent per event, recipient, channel, and configured offset.
