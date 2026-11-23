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
    ];

    private static readonly HashSet<string> Codes = All.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);

    public static bool Exists(string code) => Codes.Contains(code);
}
