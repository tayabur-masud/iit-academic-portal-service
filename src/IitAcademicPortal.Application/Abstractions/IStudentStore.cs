using IitAcademicPortal.Application.MyProfile;
using IitAcademicPortal.Application.Students;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Students;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>A Configuration record a student refers to: whether it is live (not deleted) is implied by finding it at all.</summary>
public sealed record StudentReference(
    Guid Id, string Name, bool IsActive, Guid? ParentId = null, decimal? CreditsToBeCompleted = null, EducationalInstitutionType? InstitutionType = null,
    decimal? MinimumCgpaForCompletion = null);

public enum StudentConflictKind
{
    RegistrationNumber,
    DuplicateAdmission,
    Email,
    Profile,
    Other,
}

/// <summary>Raised by the store when the database rejected a change: a duplicate that raced past the application check.</summary>
public sealed class StudentConflictException(StudentConflictKind kind, Exception inner)
    : Exception("A student rule in the database rejected the change.", inner)
{
    public StudentConflictKind Kind { get; } = kind;
}

/// <summary>
/// Persistence for Student profiles, admissions, and academic history over the request's unit of work. ASP.NET Identity saves inside
/// its own calls on the same context, so a first admission (account, role, profile, admission, history, and audit events) runs inside
/// one <see cref="BeginTransactionAsync"/> transaction and commits together or not at all.
/// </summary>
public interface IStudentStore
{
    Task<ITeacherTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>The tracked profile of an account, with its academic history, or null.</summary>
    Task<StudentProfile?> FindProfileByUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>The tracked profile with its academic history, or null.</summary>
    Task<StudentProfile?> FindProfileAsync(Guid profileId, CancellationToken cancellationToken);

    Task<StudentAdmissionRecord?> FindAdmissionAsync(Guid admissionId, CancellationToken cancellationToken);

    /// <summary>True when the profile already has an admission for this Degree and Batch, other than the excluded one.</summary>
    Task<bool> AdmissionExistsAsync(Guid profileId, Guid degreeId, Guid batchId, Guid exceptAdmissionId, CancellationToken cancellationToken);

    /// <summary>True when another admission (of any Student, active or not) has this normalized registration number.</summary>
    Task<bool> RegistrationNumberExistsAsync(string normalized, Guid exceptAdmissionId, CancellationToken cancellationToken);

    /// <summary>The live Degree with its credit requirement, or null when it does not exist or was deleted.</summary>
    /// <summary>The live Degree with its required credits and minimum CGPA for completion, or null.</summary>
    Task<StudentReference?> FindDegreeAsync(Guid degreeId, CancellationToken cancellationToken);

    /// <summary>The live Batch with its Degree as <see cref="StudentReference.ParentId"/>, or null.</summary>
    Task<StudentReference?> FindBatchAsync(Guid batchId, CancellationToken cancellationToken);

    Task<StudentReference?> FindHallAsync(Guid hallId, CancellationToken cancellationToken);

    /// <summary>The live Upazila with its District as <see cref="StudentReference.ParentId"/>, or null.</summary>
    Task<StudentReference?> FindUpazilaAsync(Guid upazilaId, CancellationToken cancellationToken);

    /// <summary>The live City Corporation with its District as <see cref="StudentReference.ParentId"/>, or null.</summary>
    Task<StudentReference?> FindCityCorporationAsync(Guid cityCorporationId, CancellationToken cancellationToken);

    /// <summary>The live educational institution with its type, or null.</summary>
    Task<StudentReference?> FindInstitutionAsync(Guid institutionId, CancellationToken cancellationToken);

    Task<StudentReference?> FindBoardAsync(Guid boardId, CancellationToken cancellationToken);

    Task<StudentReference?> FindGroupAsync(Guid groupId, CancellationToken cancellationToken);

    void Add(StudentProfile profile);

    void Add(StudentAdmissionRecord admission);

    /// <exception cref="StudentConflictException">A uniqueness rule in the database rejected the change.</exception>
    Task SaveAsync(CancellationToken cancellationToken);

    Task<StudentAdmissionDetail?> GetDetailAsync(Guid admissionId, CancellationToken cancellationToken);

    /// <summary>The profile of an account with its history and every admission, or null when the account has none.</summary>
    Task<StudentProfileDetail?> GetProfileOfUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// The account's own personal profile for My Profile: personal details, addresses with locality names, guardian, and prior education
    /// in entry order. Admissions are not loaded. Null when the account has no Student profile.
    /// </summary>
    Task<MyStudentProfile?> GetOwnProfileAsync(string userId, CancellationToken cancellationToken);

    Task<StudentListPage> ListAsync(StudentListFilter filter, int page, int pageSize, CancellationToken cancellationToken);
}
