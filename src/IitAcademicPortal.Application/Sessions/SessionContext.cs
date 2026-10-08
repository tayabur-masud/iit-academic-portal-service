namespace IitAcademicPortal.Application.Sessions;

/// <summary>The account's assigned supported roles and this session's active role.</summary>
public sealed record SessionContext(IReadOnlyList<string> AvailableRoles, string? ActiveRole);

/// <summary>A session that passed server-side validation for the current request.</summary>
public sealed record ValidatedSession(Guid SessionId, string UserId, SessionContext Context);
