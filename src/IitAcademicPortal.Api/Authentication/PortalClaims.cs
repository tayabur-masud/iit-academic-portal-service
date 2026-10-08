using System.Security.Claims;
using IitAcademicPortal.Application.Sessions;

namespace IitAcademicPortal.Api.Authentication;

/// <summary>
/// Claims issued for a validated session. Only the active role is issued as a role claim, so role-based
/// checks can never see the union of every role assigned to a multi-role account.
/// </summary>
public static class PortalClaims
{
    public const string SessionId = "iit:sid";
    public const string AvailableRole = "iit:available_role";

    public static ClaimsPrincipal CreatePrincipal(ValidatedSession session, string authenticationType)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId),
            new(SessionId, session.SessionId.ToString()),
        };
        claims.AddRange(session.Context.AvailableRoles.Select(role => new Claim(AvailableRole, role)));
        if (session.Context.ActiveRole is { } activeRole)
        {
            claims.Add(new Claim(ClaimTypes.Role, activeRole));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }

    public static Guid? GetSessionId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(SessionId), out var id) ? id : null;

    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No authenticated user.");

    public static string? GetActiveRole(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Role);

    public static SessionContext GetSessionContext(this ClaimsPrincipal user) =>
        new(user.FindAll(AvailableRole).Select(c => c.Value).ToArray(), user.GetActiveRole());
}
