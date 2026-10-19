namespace IitAcademicPortal.Domain.Students;

/// <summary>The fixed prior-education categories on the admission form.</summary>
public enum AcademicHistoryCategory
{
    SscOLevel,
    HscALevel,
    BachelorGraduation,
    Master,
    Pgdit,
    Other,
}

/// <summary>
/// One prior-education row of a <see cref="StudentProfile"/>, shared by all of that person's admissions. Only
/// <see cref="Category"/> is controlled. Every other value is an optional text snapshot exactly as written on the paper form, never a
/// reference to a Configuration catalog, so values missing from the catalogs and their original formatting survive. An IIT program
/// already recorded as an admission is not repeated here.
/// </summary>
public sealed class StudentAcademicHistory : AuditedEntity
{
    public const int MaxValueLength = 200;

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid StudentProfileId { get; private set; }

    /// <summary>The position of the row on the form, so it is shown in the order entered.</summary>
    public int SortOrder { get; set; }

    public AcademicHistoryCategory Category { get; set; }

    public string? Examination { get; set; }

    public string? SchoolOrCollege { get; set; }

    public string? Board { get; set; }

    public string? Group { get; set; }

    public string? Department { get; set; }

    public string? University { get; set; }

    public string? Subject { get; set; }

    public string? Institution { get; set; }

    public string? Year { get; set; }

    public string? MarksObtained { get; set; }

    public string? MarksPercentage { get; set; }

    public string? Cgpa { get; set; }

    public string? Class { get; set; }

    public string? Division { get; set; }

    public static StudentAcademicHistory Create(Guid studentProfileId) => new() { StudentProfileId = studentProfileId };
}
