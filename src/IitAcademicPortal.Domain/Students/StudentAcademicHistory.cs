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
/// One prior-education row of a <see cref="StudentProfile"/>, shared by all of that person's admissions. <see cref="Category"/> is
/// controlled. The school or college, university, institution, board, and group may each reference a Configuration catalog record
/// (Educational institutions, Education boards, Education groups) and always keep a name snapshot: the catalog name when the record was
/// chosen, or the name typed from the paper form when it is not in the catalog. Renaming or deactivating a catalog record later never
/// changes the snapshot. Every other value is optional text exactly as written on the form. An IIT program already recorded as an
/// admission is not repeated here.
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

    /// <summary>The School or College chosen from the catalog, when it was.</summary>
    public Guid? SchoolOrCollegeId { get; set; }

    public string? Board { get; set; }

    public Guid? BoardId { get; set; }

    public string? Group { get; set; }

    public Guid? GroupId { get; set; }

    public string? Department { get; set; }

    public string? University { get; set; }

    /// <summary>The University chosen from the catalog, when it was.</summary>
    public Guid? UniversityId { get; set; }

    public string? Subject { get; set; }

    public string? Institution { get; set; }

    /// <summary>Any institution chosen from the catalog, when it was.</summary>
    public Guid? InstitutionId { get; set; }

    public string? Year { get; set; }

    public string? MarksObtained { get; set; }

    public string? MarksPercentage { get; set; }

    public string? Cgpa { get; set; }

    public string? Class { get; set; }

    public string? Division { get; set; }

    public static StudentAcademicHistory Create(Guid studentProfileId) => new() { StudentProfileId = studentProfileId };
}
