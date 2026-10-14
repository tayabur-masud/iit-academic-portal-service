namespace IitAcademicPortal.Domain;

/// <summary>
/// The base of every business entity: who created it and when, and who last changed it and when. Derive from this and nothing
/// else is needed: the columns are mapped for you, and the database context stamps them on every insert and update with the
/// server clock and the signed-in account (never a client value), so a feature cannot forget them.
/// </summary>
/// <remarks>
/// On insert the last-modified pair is set equal to the created pair, so it is never empty. An update, including a soft delete,
/// moves only the last-modified pair. The "by" values are the stable account identifier (the same one audit history keeps), and
/// are null when no signed-in account made the change (a background job, or rows loaded by a SQL seed script). A row that
/// existed before these columns were added shows the time of that migration and no account. Entities that are deliberately not
/// derived from this class are listed, with the reason, in <c>AuditedEntityConventionTests</c>.
/// </remarks>
public abstract class AuditedEntity
{
    public const int MaxUserIdLength = 450;

    public DateTime CreatedAtUtc { get; private set; }

    public string? CreatedByUserId { get; private set; }

    public DateTime LastModifiedAtUtc { get; private set; }

    public string? LastModifiedByUserId { get; private set; }

    /// <summary>Called by the database context when the entity is first saved. Not for feature code.</summary>
    public void StampCreated(DateTime atUtc, string? byUserId)
    {
        CreatedAtUtc = atUtc;
        CreatedByUserId = byUserId;
        LastModifiedAtUtc = atUtc;
        LastModifiedByUserId = byUserId;
    }

    /// <summary>Called by the database context when a saved entity changes. Not for feature code.</summary>
    public void StampModified(DateTime atUtc, string? byUserId)
    {
        LastModifiedAtUtc = atUtc;
        LastModifiedByUserId = byUserId;
    }
}
