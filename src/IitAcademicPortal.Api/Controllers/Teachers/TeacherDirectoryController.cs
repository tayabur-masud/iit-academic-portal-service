using IitAcademicPortal.Api.Authorization;
using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Application.Teachers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers.Teachers;

/// <summary>The Admin teacher directory (search, filter, numbered pages) and deactivation. Admin only, on every request.</summary>
[ApiController]
[Authorize(Policy = PortalPolicies.AdminModule)]
[Route("api/admin/teachers")]
public sealed class TeacherDirectoryController(TeacherDirectoryService directory, TeacherLifecycleService lifecycle) : TeacherControllerBase
{
    [HttpGet]
    [ProducesResponseType<TeacherPageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] TeacherQueryString query, CancellationToken cancellationToken)
    {
        var result = await directory.ListAsync(query.ToQuery(), cancellationToken);
        return result.Succeeded ? Ok(TeacherPageResponse.From(result.Value!)) : Map(result);
    }

    /// <summary>
    /// Disables the account, makes the profile inactive, and revokes every active session. Nothing is deleted and the roles,
    /// profile, and history stay. Repeating it on a deactivated teacher changes nothing.
    /// </summary>
    [HttpPost("{teacherId:guid}/deactivation")]
    [ProducesResponseType<TeacherResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid teacherId, CancellationToken cancellationToken) =>
        Map(await lifecycle.DeactivateAsync(teacherId, cancellationToken));
}
