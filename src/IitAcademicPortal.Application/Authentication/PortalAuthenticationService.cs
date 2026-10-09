using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Sessions;
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
/// Email/password sign-in and per-session state: validation, active-role selection, and revocation.
/// Sessions expire after three hours without authenticated activity.
/// </summary>
public sealed class PortalAuthenticationService(
    UserManager<PortalUser> userManager,
    IPasswordHasher<PortalUser> passwordHasher,
    IAuthSessionRepository sessions,
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
            return SignInOutcome.Failed;
        }

        var passwordValid = await userManager.CheckPasswordAsync(user, password);
        if (!passwordValid || await userManager.IsLockedOutAsync(user))
        {
            logger.LogWarning("Sign-in failed for user {UserId}", user.Id);
            return SignInOutcome.Failed;
        }

        var roles = PortalRoles.Normalize(await userManager.GetRolesAsync(user));
        var activeRole = roles.Count == 1 ? roles[0] : null;

        var handle = SessionHandle.Create();
        var session = new AuthSession(user.Id, SessionHandle.ComputeDigest(handle), activeRole, timeProvider.GetUtcNow());
        sessions.Add(session);
        await sessions.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Session {SessionId} created for user {UserId}", session.Id, user.Id);
        return new SignInOutcome(true, handle, new SessionContext(roles, activeRole));
    }

    /// <summary>
    /// Resolves a session handle to an active session. The stored active role is honored only while it
    /// remains assigned to the account, so removed assignments stop authorizing immediately.
    /// </summary>
    public async Task<ValidatedSession?> ValidateAsync(string handle, CancellationToken cancellationToken)
    {
        var snapshot = await sessions.TouchActiveByDigestAsync(
            SessionHandle.ComputeDigest(handle), timeProvider.GetUtcNow(), cancellationToken);
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
            return new SetActiveRoleOutcome(SetActiveRoleStatus.RoleNotAssigned, ToContext(snapshot));
        }

        snapshot.Session.SetActiveRole(role);
        await sessions.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Session {SessionId} switched active role to {Role}", sessionId, role);
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
    }

    private static SessionContext ToContext(SessionSnapshot snapshot)
    {
        var roles = PortalRoles.Normalize(snapshot.AssignedRoles);
        var activeRole = roles.Contains(snapshot.Session.ActiveRole ?? string.Empty, StringComparer.Ordinal)
            ? snapshot.Session.ActiveRole
            : null;
        return new SessionContext(roles, activeRole);
    }
}
