using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Sessions;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace IitAcademicPortal.Application.Authentication;

public sealed record SignInOutcome(bool Succeeded, string? SessionHandle, SessionContext? Context)
{
    public static SignInOutcome Failed { get; } = new(false, null, null);
}

public enum SetActiveRoleStatus
{
    Updated,
    SessionNotFound,
    RoleNotAssigned,
}

public sealed record SetActiveRoleOutcome(SetActiveRoleStatus Status, SessionContext? Context);

/// <summary>
/// Email/password sign-in into the account's default role, and per-session state: validation, active-role
/// switching, and revocation.
/// Sessions expire after three hours without authenticated activity.
/// </summary>
public sealed class PortalAuthenticationService(
    UserManager<PortalUser> userManager,
    IPasswordHasher<PortalUser> passwordHasher,
    IAuthSessionRepository sessions,
    IAuditEventRecorder audit,
    TimeProvider timeProvider,
    ILogger<PortalAuthenticationService> logger)
{
    // Verified against for unknown emails so both failure paths do comparable hashing work.
    private static readonly Lazy<string> TimingEqualizerHash = new(() =>
        new PasswordHasher<PortalUser>().HashPassword(new PortalUser(), Guid.NewGuid().ToString()));

    public async Task<SignInOutcome> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            passwordHasher.VerifyHashedPassword(new PortalUser(), TimingEqualizerHash.Value, password);
            logger.LogWarning("Sign-in failed: no account matched the submitted email");
            await RecordSignInFailureAsync();
            return SignInOutcome.Failed;
        }

        var passwordValid = await userManager.CheckPasswordAsync(user, password);
        if (!passwordValid || await userManager.IsLockedOutAsync(user))
        {
            logger.LogWarning("Sign-in failed for user {UserId}", user.Id);
            await RecordSignInFailureAsync();
            return SignInOutcome.Failed;
        }

        // Every account with a role enters its default role directly; there is no role-selection step.
        var roles = PortalRoles.Normalize(await userManager.GetRolesAsync(user));
        var activeRole = PortalRoles.ResolveDefault(roles, user.DefaultRole);

        var handle = SessionHandle.Create();
        var session = new AuthSession(user.Id, SessionHandle.ComputeDigest(handle), activeRole, timeProvider.GetUtcNow());
        sessions.Add(session);
        await sessions.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Session {SessionId} created for user {UserId}", session.Id, user.Id);

        // One event per sign-in attempt: the session it created is part of this event, not a separate one.
        await audit.RecordSecurityAsync(new AuditEventRequest(AuditEventDefinitions.SignIn, AuditOutcome.Success)
        {
            ActorUserId = user.Id,
            EntityType = "Session",
            EntityId = session.Id.ToString(),
            Metadata = new Dictionary<string, object?> { ["activeRole"] = activeRole },
        });
        return new SignInOutcome(true, handle, new SessionContext(roles, activeRole));
    }

    // A failed attempt records no account: the actor is null and the submitted email is never kept. Unknown
    // email, wrong password, and lockout look identical, matching what the client is told.
    private Task RecordSignInFailureAsync() =>
        audit.RecordSecurityAsync(new AuditEventRequest(AuditEventDefinitions.SignIn, AuditOutcome.Failure)
        {
            Metadata = new Dictionary<string, object?> { ["reason"] = "invalid_credentials" },
        });

    /// <summary>
    /// Resolves a session handle to an active session. The stored active role is honored only while it
    /// remains assigned to the account, so removed assignments stop authorizing immediately; the session
    /// then continues in the account's default role among the roles it still has.
    /// </summary>
    public async Task<ValidatedSession?> ValidateAsync(string handle, CancellationToken cancellationToken)
    {
        var touch = await sessions.TouchActiveByDigestAsync(
            SessionHandle.ComputeDigest(handle), timeProvider.GetUtcNow(), cancellationToken);

        // The check itself revokes an idle session; record that once, on the request that revoked it. Ordinary
        // authenticated requests write nothing extra.
        if (touch.IdleRevoked is { } idle)
        {
            await RecordSessionRevokedAsync(idle.UserId, idle.SessionId, SessionRevocationReason.IdleTimeout);
        }

        var snapshot = touch.Active;
        return snapshot is null
            ? null
            : new ValidatedSession(snapshot.Session.Id, snapshot.Session.UserId, ToContext(snapshot));
    }

    public async Task<SetActiveRoleOutcome> SetActiveRoleAsync(Guid sessionId, string role, CancellationToken cancellationToken)
    {
        var snapshot = await sessions.FindActiveByIdAsync(sessionId, cancellationToken);
        if (snapshot is null)
        {
            return new SetActiveRoleOutcome(SetActiveRoleStatus.SessionNotFound, null);
        }

        if (!PortalRoles.Normalize(snapshot.AssignedRoles).Contains(role, StringComparer.Ordinal))
        {
            logger.LogWarning(
                "Session {SessionId} for user {UserId} was denied a switch to unassigned role {Role}",
                sessionId, snapshot.Session.UserId, role);
            await RecordRoleSwitchAsync(snapshot, role, AuditOutcome.Denied, ToContext(snapshot).ActiveRole);
            return new SetActiveRoleOutcome(SetActiveRoleStatus.RoleNotAssigned, ToContext(snapshot));
        }

        var previousRole = ToContext(snapshot).ActiveRole;
        snapshot.Session.SetActiveRole(role);
        await sessions.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Session {SessionId} switched active role to {Role}", sessionId, role);
        await RecordRoleSwitchAsync(snapshot, role, AuditOutcome.Success, previousRole);
        return new SetActiveRoleOutcome(SetActiveRoleStatus.Updated, ToContext(snapshot));
    }

    /// <summary>Revokes exactly one session; other sessions for the same account are unaffected.</summary>
    public async Task RevokeAsync(Guid sessionId, SessionRevocationReason reason, CancellationToken cancellationToken)
    {
        var snapshot = await sessions.FindActiveByIdAsync(sessionId, cancellationToken);
        if (snapshot is null)
        {
            return;
        }

        snapshot.Session.Revoke(reason, timeProvider.GetUtcNow());
        await sessions.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Session {SessionId} revoked: {Reason}", sessionId, reason);
        await RecordSessionRevokedAsync(snapshot.Session.UserId, sessionId, reason);
    }

    private Task RecordSessionRevokedAsync(string userId, Guid sessionId, SessionRevocationReason reason) =>
        audit.RecordSecurityAsync(new AuditEventRequest(AuditEventDefinitions.SessionRevoked, AuditOutcome.Success)
        {
            ActorUserId = userId,
            EntityType = "Session",
            EntityId = sessionId.ToString(),
            Metadata = new Dictionary<string, object?> { ["reason"] = reason.ToString() },
        });

    private Task RecordRoleSwitchAsync(SessionSnapshot snapshot, string requestedRole, AuditOutcome outcome, string? fromRole) =>
        audit.RecordSecurityAsync(new AuditEventRequest(AuditEventDefinitions.RoleSwitched, outcome)
        {
            ActorUserId = snapshot.Session.UserId,
            EntityType = "Session",
            EntityId = snapshot.Session.Id.ToString(),
            Metadata = new Dictionary<string, object?> { ["fromRole"] = fromRole, ["toRole"] = requestedRole },
        });

    private static SessionContext ToContext(SessionSnapshot snapshot)
    {
        var roles = PortalRoles.Normalize(snapshot.AssignedRoles);
        var activeRole = roles.Contains(snapshot.Session.ActiveRole ?? string.Empty, StringComparer.Ordinal)
            ? snapshot.Session.ActiveRole
            : PortalRoles.ResolveDefault(roles, snapshot.DefaultRole);
        return new SessionContext(roles, activeRole);
    }
}
