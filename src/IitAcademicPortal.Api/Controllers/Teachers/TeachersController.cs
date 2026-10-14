using IitAcademicPortal.Api.Authorization;
using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Application.Teachers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers.Teachers;

/// <summary>
/// Teacher Management for Admins: add, read, update, and change the roles of one teacher. Every action requires the session's active
/// Admin role, checked on each request; a state-changing call also needs the anti-forgery token. There is no delete: a teacher is
/// deactivated (see <see cref="TeacherDirectoryController"/>).
/// </summary>
[ApiController]
[Authorize(Policy = PortalPolicies.AdminModule)]
[Route("api/admin/teachers")]
public sealed class TeachersController(
    TeacherProvisioningService provisioning,
    TeacherProfileService profiles,
    TeacherRoleService roles) : TeacherControllerBase
{
    [HttpPost]
    [ProducesResponseType<TeacherResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateTeacherRequest request, CancellationToken cancellationToken) =>
        Map(await provisioning.CreateAsync(request.ToInput(), cancellationToken));

    [HttpGet("{teacherId:guid}", Name = "GetTeacher")]
    [ProducesResponseType<TeacherResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid teacherId, CancellationToken cancellationToken) =>
        Map(await profiles.GetAsync(teacherId, cancellationToken));

    [HttpPut("{teacherId:guid}")]
    [ProducesResponseType<TeacherResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid teacherId, [FromBody] UpdateTeacherRequest request, CancellationToken cancellationToken) =>
        Map(await profiles.UpdateAsync(teacherId, request.ToInput(), cancellationToken));

    [HttpPut("{teacherId:guid}/roles")]
    [ProducesResponseType<TeacherResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateRoles(Guid teacherId, [FromBody] TeacherRolesRequest request, CancellationToken cancellationToken) =>
        Map(await roles.UpdateAsync(teacherId, request.ToInput(), cancellationToken));
}
