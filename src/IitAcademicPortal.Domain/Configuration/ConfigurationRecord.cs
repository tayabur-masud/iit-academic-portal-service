namespace IitAcademicPortal.Domain.Configuration;

/// <summary>
/// What every configuration record shares: a server-generated identifier, the name as entered, its normalized
/// comparison form, and an active flag. Records are deactivated, never cascaded, and physically deleted only
/// when nothing refers to them.
/// </summary>
public abstract class ConfigurationRecord : AuditedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// True once an Admin has deleted the record. A deleted record is kept in the database (so history and audit stay
    /// meaningful) but is hidden from every list, lookup, and selector, and its name and code can be used again.
    /// </summary>
    public bool IsDeleted { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    /// <summary>The stable identifier of the account that deleted the record.</summary>
    public string? DeletedByUserId { get; private set; }

    public void MarkDeleted(DateTime atUtc, string? byUserId)
    {
        IsDeleted = true;
        DeletedAtUtc = atUtc;
        DeletedByUserId = byUserId;
    }

    /// <summary>Sets the display name (trimmed) and its comparison form together.</summary>
    public void Rename(string? name)
    {
        Name = NameNormalizer.Display(name);
        NormalizedName = NameNormalizer.Normalize(name);
    }
}

/// <summary>A record with an optional code that is unique within its own catalog when present.</summary>
public interface ICodedRecord
{
    string? Code { get; }

    string? NormalizedCode { get; }

    void SetCode(string? code);
}
