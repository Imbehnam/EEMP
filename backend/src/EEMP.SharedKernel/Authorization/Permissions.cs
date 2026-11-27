namespace EEMP.SharedKernel.Authorization;

public sealed record PermissionDefinition(string Code, string Module, bool IsSensitive, bool BranchScoped);

/// <summary>Code-defined permission catalog. Roles are tenant-defined bundles of these codes.</summary>
public static class Permissions
{
    public const string OrganizationRead = "organization.read";
    public const string OrganizationManage = "organization.manage";
    public const string BranchesRead = "branches.read";
    public const string BranchesManage = "branches.manage";
    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string StudentsRead = "students.read";
    public const string StudentsManage = "students.manage";
    public const string GuardiansRead = "guardians.read";
    public const string GuardiansManage = "guardians.manage";
    public const string TeachersRead = "teachers.read";
    public const string TeachersManage = "teachers.manage";
    public const string StaffRead = "staff.read";
    public const string StaffManage = "staff.manage";
    public const string AuditRead = "audit.read";
    public const string CatalogRead = "catalog.read";
    public const string CatalogManage = "catalog.manage";
    public const string ClassesRead = "classes.read";
    public const string ClassesManage = "classes.manage";
    public const string EnrollmentsRead = "enrollments.read";
    public const string EnrollmentsManage = "enrollments.manage";
    public const string AttendanceRead = "attendance.read";
    public const string AttendanceManage = "attendance.manage";
    public const string GradesRead = "grades.read";
    public const string GradesManage = "grades.manage";
    public const string GradesApprove = "grades.approve";
    public const string GradesPublish = "grades.publish";
    public const string ReportCardsRead = "reportcards.read";
    public const string ReportCardsManage = "reportcards.manage";
    public const string ReportCardsPublish = "reportcards.publish";
    public const string FinanceSetup = "finance.setup.manage";
    public const string FinanceRead = "finance.read";
    public const string FinanceInvoicesManage = "finance.invoices.manage";
    public const string FinancePaymentsManage = "finance.payments.manage";
    public const string FinanceApprove = "finance.approve";
    public const string FinanceReports = "finance.reports.read";
    public const string WorkflowDefinitionsRead = "workflow.definitions.read";
    public const string WorkflowDefinitionsManage = "workflow.definitions.manage";
    public const string WorkflowInstancesRead = "workflow.instances.read";
    public const string WorkflowInstancesManage = "workflow.instances.manage";
    public const string WorkflowTasksApprove = "workflow.tasks.approve";
    public const string ConsultationConsultantsManage = "consultation.consultants.manage";
    public const string ConsultationConsult = "consultation.consult";
    public const string ConsultationBookingsRead = "consultation.bookings.read";
    public const string ConsultationBookingsManage = "consultation.bookings.manage";
    public const string ConsultationNotesRead = "consultation.notes.read";
    public const string SupportTicketsRead = "support.tickets.read";
    public const string SupportTicketsManage = "support.tickets.manage";
    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(OrganizationRead, "organization", false, false),
        new(OrganizationManage, "organization", false, false),
        new(BranchesRead, "organization", false, true),
        new(BranchesManage, "organization", false, true),
        new(RolesRead, "identity", false, false),
        new(RolesManage, "identity", false, false),
        new(UsersRead, "identity", false, false),
        new(UsersManage, "identity", false, false),
        new(AuditRead, "audit", false, false),
        new(StudentsRead, "people", true, true),
        new(StudentsManage, "people", true, true),
        new(GuardiansRead, "people", true, true),
        new(GuardiansManage, "people", true, true),
        new(TeachersRead, "people", true, true),
        new(TeachersManage, "people", true, true),
        new(StaffRead, "people", true, true),
        new(StaffManage, "people", true, true),
        new(CatalogRead, "academic", false, false),
        new(CatalogManage, "academic", false, false),
        new(ClassesRead, "academic", false, true),
        new(ClassesManage, "academic", false, true),
        new(EnrollmentsRead, "academic", true, true),
        new(EnrollmentsManage, "academic", true, true),
        new(AttendanceRead, "academic", true, true),
        new(AttendanceManage, "academic", true, true),
        new(GradesRead, "academic", true, true),
        new(GradesManage, "academic", true, true),
        new(GradesApprove, "academic", true, true),
        new(GradesPublish, "academic", true, true),
        new(ReportCardsRead, "academic", true, true),
        new(ReportCardsManage, "academic", true, true),
        new(ReportCardsPublish, "academic", true, true),
        new(FinanceSetup, "finance", false, false),
        new(FinanceRead, "finance", true, true),
        new(FinanceInvoicesManage, "finance", true, true),
        new(FinancePaymentsManage, "finance", true, true),
        new(FinanceApprove, "finance", true, true),
        new(FinanceReports, "finance", true, true),
        new(WorkflowDefinitionsRead, "workflow", false, false),
        new(WorkflowDefinitionsManage, "workflow", false, false),
        new(WorkflowInstancesRead, "workflow", true, true),
        new(WorkflowInstancesManage, "workflow", true, true),
        new(WorkflowTasksApprove, "workflow", true, true),
        new(ConsultationConsultantsManage, "consultation", false, true),
        new(ConsultationConsult, "consultation", true, true),
        new(ConsultationBookingsRead, "consultation", true, true),
        new(ConsultationBookingsManage, "consultation", true, true),
        new(ConsultationNotesRead, "consultation", true, true),
        new(SupportTicketsRead, "support", true, true),
        new(SupportTicketsManage, "support", true, true),
    ];

    private static readonly HashSet<string> Codes = All.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);

    public static bool Exists(string code) => Codes.Contains(code);
}
