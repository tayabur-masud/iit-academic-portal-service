using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Domain.Identity;

/// <summary>
/// A portal account managed by ASP.NET Core Identity. The account email is the sign-in and
/// password-recovery address; for Students it is the personal email recorded for the account.
/// </summary>
public class PortalUser : IdentityUser
{
}
