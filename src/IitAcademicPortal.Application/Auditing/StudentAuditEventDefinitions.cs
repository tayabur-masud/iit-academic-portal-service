namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// Business events owned by Student Management, named <c>student.{action}</c>. The program and institutional identifiers keep their
/// before and after values; every personal or contact value (names, birth data, mobile and cell numbers, addresses, guardian, and
/// academic history) is recorded as "changed" only. No password, setup proof, or setup link is ever part of these events.
/// </summary>
public static class StudentAuditEventDefinitions
{
    public const string ProfileEntity = "StudentProfile";
    public const string AdmissionEntity = "StudentAdmission";

    public static readonly string[] ApprovedFields =
        ["degreeId", "batchId", "assignedHallId", "registrationNumber", "admissionSerialNumber", "admissionExamRoll", "classRoll"];

    public static readonly string[] IndicatorFields =
    [
        "applicantName", "fatherName", "motherName", "dateOfBirth", "placeOfBirth", "maritalStatus", "nationality", "applicantMobile",
        "mailingAddress", "permanentAddress", "guardian", "academicHistory",
    ];

    /// <summary>A Student profile was created (with a new account, or attached to an existing Student account).</summary>
    public static AuditEventDefinition ProfileCreated { get; } = Define("student.profile.created");

    /// <summary>Shared profile values or the academic history changed.</summary>
    public static AuditEventDefinition ProfileUpdated { get; } = Define("student.profile.updated");

    public static AuditEventDefinition AdmissionCreated { get; } = Define("student.admission.created");

    public static AuditEventDefinition AdmissionUpdated { get; } = Define("student.admission.updated");

    private static AuditEventDefinition Define(string eventType) =>
        AuditEventDefinition.Business(eventType, ApprovedFields).WithChangeIndicators(IndicatorFields);
}
