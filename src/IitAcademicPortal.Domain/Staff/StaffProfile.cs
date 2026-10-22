using IitAcademicPortal.Domain.Configuration;

namespace IitAcademicPortal.Domain.Staff;

/// <summary>
/// One Admin staff member's profile, linked one-to-one to a portal account (created with it, or an existing Admin account it was linked
/// to): a Name, a Mobile number, and a Designation from the Configuration catalog. Credentials and roles stay with the account. The profile has no verified
/// state and keeps no document: the Admin's confirmation of manual verification is recorded with each change's audit event instead.
/// Name maximum 200 characters; Mobile number maximum 50 characters. A profile is never deleted: deactivating a staff member marks it
/// inactive (and disables its account) so its history survives, and Staff Management cannot reactivate it.
/// </summary>
public sealed class StaffProfile : AuditedEntity
{
    public const int MaxNameLength = 200;
    public const int MaxMobileNumberLength = 50;

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string UserId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Contact number as entered and checked by an Admin; never represented as verified by the portal.</summary>
    public string MobileNumber { get; private set; } = string.Empty;

    public Guid DesignationId { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static StaffProfile Create(string userId, string name, string mobileNumber, Guid designationId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A staff profile belongs to a portal account.", nameof(userId));
        }

        var profile = new StaffProfile { UserId = userId };
        profile.Update(name, mobileNumber, designationId);
        return profile;
    }

    public void Update(string name, string mobileNumber, Guid designationId)
    {
        var cleanName = NameNormalizer.Display(name);
        var cleanMobile = NameNormalizer.Display(mobileNumber);
        if (cleanName.Length is 0 or > MaxNameLength)
        {
            throw new ArgumentException($"The name is required and can have at most {MaxNameLength} characters.", nameof(name));
        }

        if (cleanMobile.Length is 0 or > MaxMobileNumberLength)
        {
            throw new ArgumentException($"The mobile number is required and can have at most {MaxMobileNumberLength} characters.", nameof(mobileNumber));
        }

        if (designationId == Guid.Empty)
        {
            throw new ArgumentException("A staff profile needs a designation.", nameof(designationId));
        }

        (Name, MobileNumber, DesignationId) = (cleanName, cleanMobile, designationId);
    }

    /// <summary>Marks the profile inactive. There is no reactivation in Staff Management.</summary>
    public void Deactivate() => IsActive = false;
}
