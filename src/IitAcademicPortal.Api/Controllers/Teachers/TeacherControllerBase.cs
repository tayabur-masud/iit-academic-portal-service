using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Application.Teachers;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers.Teachers;

/// <summary>Maps a teacher operation's result to the HTTP response the contract promises.</summary>
public abstract class TeacherControllerBase : ControllerBase
{
    protected IActionResult Map<T>(TeacherResult<T> result) => result.Outcome switch
    {
        TeacherOutcome.Ok when result.Value is TeacherDetail detail => Ok(TeacherResponse.From(detail)),
        TeacherOutcome.Created when result.Value is TeacherDetail detail =>
            Created($"/api/admin/teachers/{detail.Id}", TeacherResponse.From(detail)),
        TeacherOutcome.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "No teacher has that identifier."),
        TeacherOutcome.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Not allowed", detail: result.Message),
        // The keys are the camelCase field names; building the response directly keeps them from being re-cased.
        TeacherOutcome.Invalid => ValidationProblem(new ValidationProblemDetails(result.Errors!.ToDictionary(e => e.Key, e => e.Value))),
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError),
    };
}
