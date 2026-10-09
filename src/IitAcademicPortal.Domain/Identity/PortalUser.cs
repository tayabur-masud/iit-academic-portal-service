using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Domain.Identity;

/// <summary>
/// A portal account managed by ASP.NET Core Identity. The account email is the sign-in and
/// password-recovery address; for Students it is the personal email recorded for the account.
/// </summary>
public class PortalUser : IdentityUser
{
    /// <summary>
    /// The role a multi-role account enters at sign-in. It is honored only while it is one of the account's
    /// assigned roles; otherwise <see cref="PortalRoles.ResolveDefault"/> falls back to a fixed order.
    /// Set during provisioning; administrators will change it through user management.
    /// </summary>
    public string? DefaultRole { get; set; }
}
