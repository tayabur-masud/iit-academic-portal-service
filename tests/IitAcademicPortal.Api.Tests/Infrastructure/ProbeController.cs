using IitAcademicPortal.Api.Authorization;
using IitAcademicPortal.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>
/// Test-only endpoints standing in for future Admin/Student/Teacher/Coordinator module capabilities.
/// Successful responses carry a marker so tests can assert denials never include protected data.
/// </summary>
[ApiController]
[Route("test-probe")]
public sealed class ProbeController(IAuthorizationService authorization) : ControllerBase
{
    public const string ProtectedMarker = "protected-record-content";

    [HttpGet("modules/admin")]
    [Authorize(Policy = PortalPolicies.AdminModule)]
    public IActionResult AdminModule() => Ok(new { data = ProtectedMarker });

    [HttpGet("modules/student")]
    [Authorize(Policy = PortalPolicies.StudentModule)]
    public IActionResult StudentModule() => Ok(new { data = ProtectedMarker });

    [HttpGet("modules/teacher")]
    [Authorize(Policy = PortalPolicies.TeacherModule)]
    public IActionResult TeacherModule() => Ok(new { data = ProtectedMarker });

    [HttpGet("modules/coordinator")]
    [Authorize(Policy = PortalPolicies.CoordinatorModule)]
    public IActionResult CoordinatorModule() => Ok(new { data = ProtectedMarker });

    [HttpGet("student-records/{studentUserId}")]
    public Task<IActionResult> StudentRecord(string studentUserId) =>
        AuthorizeRecord(new OwnedRecord(studentUserId));

    [HttpGet("courses/{teacherUserId}")]
    public Task<IActionResult> Course(string teacherUserId) =>
        AuthorizeRecord(new CourseRecord([teacherUserId]));

    [HttpGet("batches/{coordinatorUserId}")]
    public Task<IActionResult> Batch(string coordinatorUserId) =>
        AuthorizeRecord(new BatchRecord([coordinatorUserId]));

    [HttpGet("results/{studentUserId}/{coordinatorUserId}")]
    public Task<IActionResult> Result(string studentUserId, string coordinatorUserId) =>
        AuthorizeRecord(new StudentResultRecord(studentUserId, [coordinatorUserId]));

    private async Task<IActionResult> AuthorizeRecord(object record)
    {
        var result = await authorization.AuthorizeAsync(User, record, PortalPolicies.RecordAccess);
        return result.Succeeded ? Ok(new { data = ProtectedMarker }) : Forbid();
    }

    private sealed record OwnedRecord(string StudentUserId) : IStudentOwnedRecord;

    private sealed record CourseRecord(IReadOnlyCollection<string> AssignedTeacherUserIds) : ITeacherAssignedRecord;

    private sealed record BatchRecord(IReadOnlyCollection<string> AssignedCoordinatorUserIds) : ICoordinatorAssignedRecord;

    private sealed record StudentResultRecord(string StudentUserId, IReadOnlyCollection<string> AssignedCoordinatorUserIds)
        : IStudentOwnedRecord, ICoordinatorAssignedRecord;
}
