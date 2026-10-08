using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Application.PasswordRecovery;

/// <summary>Applies <see cref="PasswordPolicy"/> wherever Identity sets a password.</summary>
public sealed class PortalPasswordValidator : IPasswordValidator<PortalUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<PortalUser> manager, PortalUser user, string? password)
    {
        var errors = PasswordPolicy.Validate(password);
        return Task.FromResult(errors.Count == 0
            ? IdentityResult.Success
            : IdentityResult.Failed(errors.Select(e => new IdentityError { Code = "PasswordPolicy", Description = e }).ToArray()));
    }
}
