using IitAcademicPortal.Api.Authentication;
using IitAcademicPortal.Application.Authorization;
using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace IitAcademicPortal.Api.Authorization;

/// <summary>
/// Server-side authorization policies. Module policies check the session's active role (the only role
/// claim issued); <see cref="RecordAccess"/> applies the record boundaries from the approved feature list
/// through resource-based authorization.
/// </summary>
public static class PortalPolicies
{
    public const string AdminModule = "AdminModule";
    public const string StudentModule = "StudentModule";
    public const string TeacherModule = "TeacherModule";
    public const string CoordinatorModule = "CoordinatorModule";

    /// <summary>Use with <c>IAuthorizationService.AuthorizeAsync(User, record, RecordAccess)</c>.</summary>
    public const string RecordAccess = "RecordAccess";

    public static AuthorizationBuilder AddPortalPolicies(this AuthorizationBuilder builder) => builder
        // Secure by default: every endpoint requires an authenticated session unless marked [AllowAnonymous].
        .SetFallbackPolicy(new AuthorizationPolicyBuilder(PortalSessionAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .Build())
        .AddPolicy(AdminModule, p => p.RequireAuthenticatedUser().RequireRole(PortalRoles.Admin))
        .AddPolicy(StudentModule, p => p.RequireAuthenticatedUser().RequireRole(PortalRoles.Student))
        .AddPolicy(TeacherModule, p => p.RequireAuthenticatedUser().RequireRole(PortalRoles.Teacher))
        .AddPolicy(CoordinatorModule, p => p.RequireAuthenticatedUser().RequireRole(PortalRoles.Coordinator))
        .AddPolicy(RecordAccess, p => p.RequireAuthenticatedUser().AddRequirements(new RecordAccessRequirement()));
}

public sealed class RecordAccessRequirement : IAuthorizationRequirement;

public sealed class RecordAccessHandler : AuthorizationHandler<RecordAccessRequirement, object>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RecordAccessRequirement requirement, object resource)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && RecordAccessRules.CanAccess(context.User.GetActiveRole(), context.User.GetUserId(), resource))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
