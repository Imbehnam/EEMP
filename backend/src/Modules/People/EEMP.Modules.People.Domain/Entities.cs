using EEMP.SharedKernel.Domain;

namespace EEMP.Modules.People.Domain;

public enum Gender
{
    Male,
    Female,
    Other,
}

public enum PersonStatus
{
    Active,
    Inactive,
}

public enum GuardianRelationship
{
    Father,
    Mother,
    LegalGuardian,
    Grandparent,
    Sibling,
    Other,
}

/// <summary>Common person-profile fields. A profile may be linked to a user account (UserId) for portal access.</summary>
public abstract class PersonProfile : TenantEntity
{
    public Guid BranchId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? NationalId { get; set; }
    public DateOnly? BirthDate { get; set; }
    public Gender? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public PersonStatus Status { get; set; } = PersonStatus.Active;
    public Guid? UserId { get; set; }
}

public sealed class Student : PersonProfile
{
    public string StudentNumber { get; set; } = string.Empty;
    public List<StudentGuardian> Guardians { get; set; } = [];
}

public sealed class Guardian : PersonProfile;

public sealed class Teacher : PersonProfile
{
    public string EmployeeNumber { get; set; } = string.Empty;
    public string? Subjects { get; set; }
}

public sealed class StaffMember : PersonProfile
{
    public string EmployeeNumber { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
}

/// <summary>Many-to-many guardian ↔ student relationship with authority attributes.</summary>
public sealed class StudentGuardian : ITenantOwned
{
    public Guid TenantId { get; set; }
    public Guid StudentId { get; set; }
    public Guid GuardianId { get; set; }
    public GuardianRelationship Relationship { get; set; }
    public bool IsPrimary { get; set; }
}
