using IitAcademicPortal.Api.Authorization;
using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Application.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers.Students;

/// <summary>
/// Student Management for Admins: the admission directory, the email lookup used by the admission form, creating and updating an
/// admission with its shared Student profile, and graduation eligibility. Every action requires the session's active Admin role,
/// checked on each request; a state-changing call also needs the anti-forgery token. There is no delete, and no response carries a
/// password, setup proof, or setup link.
/// </summary>
[ApiController]
[Authorize(Policy = PortalPolicies.AdminModule)]
[Route("api/admin/students")]
public sealed class StudentsController(StudentAdmissionService admissions, GraduationEligibilityService eligibility) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<StudentAdmissionPageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] StudentQueryString query, CancellationToken cancellationToken)
    {
        var result = await admissions.ListAsync(query.ToQuery(), cancellationToken);
        return result.Succeeded ? Ok(StudentAdmissionPageResponse.From(result.Value!)) : Map(result);
    }

    /// <summary>Whether an admission for this email would create a Student, reuse one (with its profile), or be refused.</summary>
    [HttpGet("lookup")]
    [ProducesResponseType<StudentLookupResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Lookup([FromQuery] string? email, CancellationToken cancellationToken)
    {
        var result = await admissions.LookupAsync(email, cancellationToken);
        return result.Succeeded ? Ok(StudentLookupResponse.From(result.Value!)) : Map(result);
    }

    /// <summary>
    /// Creates an admission. A new email creates a pending Student account and profile and requests its setup email after the commit;
    /// a Student's email reuses that account and profile without another setup email.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<StudentAdmissionCreatedResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateStudentAdmissionRequest request, CancellationToken cancellationToken)
    {
        var result = await admissions.CreateAsync(request.ToCreateInput(), cancellationToken);
        return result.Succeeded
            ? Created($"/api/admin/students/{result.Value!.Admission.Id}", StudentAdmissionCreatedResponse.From(result.Value))
            : Map(result);
    }

    [HttpGet("{admissionId:guid}")]
    [ProducesResponseType<StudentAdmissionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid admissionId, CancellationToken cancellationToken) =>
        Map(await admissions.GetAsync(admissionId, cancellationToken));

    /// <summary>Replaces the shared profile, its academic history, and this admission's fields.</summary>
    [HttpPut("{admissionId:guid}")]
    [ProducesResponseType<StudentAdmissionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid admissionId, [FromBody] StudentAdmissionRequest request, CancellationToken cancellationToken) =>
        Map(await admissions.UpdateAsync(admissionId, request.ToInput(), cancellationToken));

    /// <summary>Computed on request; it never blocks or changes result calculation or publication.</summary>
    [HttpGet("{admissionId:guid}/graduation-eligibility")]
    [ProducesResponseType<GraduationEligibilityResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GraduationEligibility(Guid admissionId, CancellationToken cancellationToken)
    {
        var result = await eligibility.EvaluateAsync(admissionId, cancellationToken);
        return result.Succeeded ? Ok(GraduationEligibilityResponse.From(result.Value!)) : Map(result);
    }

    private IActionResult Map<T>(StudentResult<T> result) => result.Outcome switch
    {
        StudentOutcome.Ok when result.Value is StudentAdmissionDetail detail => Ok(StudentAdmissionResponse.From(detail)),
        StudentOutcome.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "No student admission has that identifier."),
        StudentOutcome.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Not allowed", detail: result.Message),
        // The keys are the camelCase field names; building the response directly keeps them from being re-cased.
        StudentOutcome.Invalid => ValidationProblem(new ValidationProblemDetails(result.Errors!.ToDictionary(e => e.Key, e => e.Value))),
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError),
    };
}
