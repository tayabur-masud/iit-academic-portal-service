namespace IitAcademicPortal.Domain.Students;

/// <summary>
/// One program enrollment of a <see cref="StudentProfile"/>: a Degree and a Batch of that Degree, the admission-form identifiers, and
/// the identifiers assigned later (Hall and registration number), which graduation eligibility requires. A profile has at most one
/// admission per Degree and Batch. Serial number, exam roll, class roll, and registration number are separate values. Completed
/// credits are never stored here; they are read from the academic-results owner per admission.
/// </summary>
public sealed class StudentAdmissionRecord : AuditedEntity
{
    public const int MaxIdentifierLength = 100;

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid StudentProfileId { get; private set; }

    public Guid DegreeId { get; set; }

    public Guid BatchId { get; set; }

    public string AdmissionSerialNumber { get; set; } = string.Empty;

    /// <summary>The admission test roll number ("Exam Roll" on the paper form).</summary>
    public string AdmissionExamRoll { get; set; } = string.Empty;

    public string? ClassRoll { get; set; }

    public Guid? AssignedHallId { get; set; }

    public string? RegistrationNumber { get; private set; }

    /// <summary>Trimmed, single-spaced, upper-case registration number; unique across all admissions when present.</summary>
    public string? NormalizedRegistrationNumber { get; private set; }

    public static StudentAdmissionRecord Create(Guid studentProfileId) => new() { StudentProfileId = studentProfileId };

    public void SetRegistrationNumber(string? display, string? normalized)
    {
        RegistrationNumber = display;
        NormalizedRegistrationNumber = normalized;
    }
}
