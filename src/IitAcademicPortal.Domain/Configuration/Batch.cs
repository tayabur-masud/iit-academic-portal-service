namespace IitAcademicPortal.Domain.Configuration;

/// <summary>
/// A cohort of one Degree that starts in one Semester and belongs to one Academic Session. Its name is unique
/// within its Degree; creating one assigns no students and advances no semester.
/// </summary>
public sealed class Batch : ConfigurationRecord
{
    public Guid DegreeId { get; set; }

    public Guid StartingSemesterId { get; set; }

    public Guid AcademicSessionId { get; set; }
}
