namespace EEMP.Modules.People.Application;

/// <summary>Fields shared by every person request. Dates are yyyy-MM-dd; enums are camelCase strings.</summary>
public interface IPersonRequest
{
    Guid? BranchId { get; }
    string? FirstName { get; }
    string? LastName { get; }
    string? NationalId { get; }
    string? BirthDate { get; }
    string? Gender { get; }
    string? Phone { get; }
    string? Email { get; }
    string? Status { get; }
    Guid? UserId { get; }
    string? RowVersion { get; }
}

public sealed record StudentGuardianDto(Guid GuardianId, string Relationship, bool IsPrimary);
public sealed record StudentGuardianDetailDto(Guid GuardianId, string Relationship, bool IsPrimary, string FirstName, string LastName, string? Phone, Guid BranchId);
public sealed record StudentGuardianRequest(Guid GuardianId, string? Relationship, bool IsPrimary);

public sealed record StudentDto(Guid Id, Guid BranchId, string FirstName, string LastName, string? NationalId, string? BirthDate, string? Gender,
    string? Phone, string? Email, string Status, Guid? UserId, string StudentNumber, IReadOnlyList<StudentGuardianDto> Guardians, string RowVersion);
public sealed record GuardianDto(Guid Id, Guid BranchId, string FirstName, string LastName, string? NationalId, string? BirthDate, string? Gender,
    string? Phone, string? Email, string Status, Guid? UserId, string RowVersion);
public sealed record TeacherDto(Guid Id, Guid BranchId, string FirstName, string LastName, string? NationalId, string? BirthDate, string? Gender,
    string? Phone, string? Email, string Status, Guid? UserId, string EmployeeNumber, string? Subjects, string RowVersion);
public sealed record StaffDto(Guid Id, Guid BranchId, string FirstName, string LastName, string? NationalId, string? BirthDate, string? Gender,
    string? Phone, string? Email, string Status, Guid? UserId, string EmployeeNumber, string Position, string RowVersion);

public sealed record StudentRequest(Guid? BranchId, string? FirstName, string? LastName, string? NationalId, string? BirthDate, string? Gender,
    string? Phone, string? Email, string? Status, Guid? UserId, string? StudentNumber, IReadOnlyList<StudentGuardianRequest>? Guardians, string? RowVersion) : IPersonRequest;
public sealed record GuardianRequest(Guid? BranchId, string? FirstName, string? LastName, string? NationalId, string? BirthDate, string? Gender,
    string? Phone, string? Email, string? Status, Guid? UserId, string? RowVersion) : IPersonRequest;
public sealed record TeacherRequest(Guid? BranchId, string? FirstName, string? LastName, string? NationalId, string? BirthDate, string? Gender,
    string? Phone, string? Email, string? Status, Guid? UserId, string? EmployeeNumber, string? Subjects, string? RowVersion) : IPersonRequest;
public sealed record StaffRequest(Guid? BranchId, string? FirstName, string? LastName, string? NationalId, string? BirthDate, string? Gender,
    string? Phone, string? Email, string? Status, Guid? UserId, string? EmployeeNumber, string? Position, string? RowVersion) : IPersonRequest;

public sealed record PortalProfileDto(string Kind, object Profile);
public sealed record PortalStudentDto(Guid Id, Guid BranchId, string FirstName, string LastName, string StudentNumber, string Relationship, bool IsPrimary);
