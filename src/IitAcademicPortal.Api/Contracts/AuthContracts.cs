using System.ComponentModel.DataAnnotations;
using IitAcademicPortal.Application.Sessions;

namespace IitAcademicPortal.Api.Contracts;

// Request/response DTOs for contracts/authentication.openapi.json. Unknown JSON members are rejected
// globally (additionalProperties: false).

public sealed class SignInRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;
}

public sealed class ActiveRoleRequest
{
    [Required]
    public string Role { get; init; } = string.Empty;
}

public sealed class PasswordResetRequest
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; init; } = string.Empty;
}

public sealed class PasswordResetCompletion
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Proof { get; init; } = string.Empty;

    [Required]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed record SessionContextResponse(IReadOnlyList<string> AvailableRoles, string? ActiveRole)
{
    public static SessionContextResponse From(SessionContext context) => new(context.AvailableRoles, context.ActiveRole);
}

public sealed record AntiForgeryTokenResponse(string RequestToken);

public sealed record GenericResetAcceptedResponse(string Message);
