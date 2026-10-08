namespace IitAcademicPortal.Domain.Authorization;

// Record-boundary markers from the approved feature list. Academic records (results, payments,
// courses, assessment components, batches) implement whichever boundaries apply to them; the
// authorization rules then decide access for the session's active role only.

/// <summary>A record that belongs to one student (academic or payment information).</summary>
public interface IStudentOwnedRecord
{
    string StudentUserId { get; }
}

/// <summary>A course or assessment component limited to its assigned teachers.</summary>
public interface ITeacherAssignedRecord
{
    IReadOnlyCollection<string> AssignedTeacherUserIds { get; }
}

/// <summary>A batch, or a student result/report within a batch, limited to its assigned coordinators.</summary>
public interface ICoordinatorAssignedRecord
{
    IReadOnlyCollection<string> AssignedCoordinatorUserIds { get; }
}
