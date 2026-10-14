namespace IitAcademicPortal.Domain.Identity;

/// <summary>
/// Whether a portal account may authenticate. Only <see cref="Active"/> accounts can sign in or keep using a session.
/// </summary>
/// <remarks>
/// <see cref="Active"/> is deliberately the zero value: it is the column's default, and EF Core treats the CLR default of a
/// property that has a database default as "not set", so a non-default first value would be silently stored as Active.
/// The column holds the name, so the order is not stored.
/// </remarks>
public enum TeacherAccountStatus
{
    /// <summary>May sign in. Every account that existed before account status was introduced is Active.</summary>
    Active,

    /// <summary>Created by an Admin; cannot sign in until credential setup activates it (a later feature).</summary>
    PendingCredentialSetup,

    /// <summary>Disabled by an Admin. Roles, profile, and history are kept; sign-in and sessions are refused.</summary>
    Deactivated,
}
