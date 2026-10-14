using System.ComponentModel.DataAnnotations;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Teachers;

namespace IitAcademicPortal.Application.Teachers;

/// <summary>Rules and audit helpers shared by the Teacher Management services.</summary>
internal static class TeacherSupport
{
    public const int MaxEmailLength = 256;

    public static void Validate(TeacherInput input, TeacherErrors errors)
    {
        var name = NameNormalizer.Display(input.DisplayName);
        if (name.Length == 0)
        {
            errors.Add("displayName", "Enter the name of the teacher.");
        }
        else if (name.Length > NameNormalizer.MaxNameLength)
        {
            errors.Add("displayName", $"The name can have at most {NameNormalizer.MaxNameLength} characters.");
        }

        var email = (input.Email ?? string.Empty).Trim();
        if (email.Length == 0)
        {
            errors.Add("email", "Enter the email address of the teacher.");
        }
        else if (email.Length > MaxEmailLength || !new EmailAddressAttribute().IsValid(email))
        {
            errors.Add("email", "Enter a valid email address.");
        }

        if ((input.EmployeeStaffId?.Trim().Length ?? 0) > NameNormalizer.MaxCodeLength)
        {
            errors.Add("employeeStaffId", $"The staff ID can have at most {NameNormalizer.MaxCodeLength} characters.");
        }

        if ((input.Phone?.Trim().Length ?? 0) > TeacherProfile.MaxPhoneLength)
        {
            errors.Add("phone", $"The phone number can have at most {TeacherProfile.MaxPhoneLength} characters.");
        }
    }

    /// <summary>The staff ID rule (unique among all profiles) and the Designation rule (a live, active one when newly chosen).</summary>
    public static async Task ValidateReferencesAsync(
        TeacherInput input, Guid profileId, Guid? currentDesignationId, ITeacherStore store, TeacherErrors errors, CancellationToken cancellationToken)
    {
        if (NameNormalizer.NormalizeCode(input.EmployeeStaffId) is { } staffId
            && !errors.Has("employeeStaffId")
            && await store.StaffIdExistsAsync(staffId, profileId, cancellationToken))
        {
            errors.Add("employeeStaffId", "Another teacher already has this staff ID.");
        }

        if (input.DesignationId is { } designationId && designationId != currentDesignationId
            && !await store.DesignationAvailableAsync(designationId, cancellationToken))
        {
            errors.Add("designationId", "Choose an active designation.");
        }
    }

    /// <summary>Adds the current email of the accounts that created and last changed the profile.</summary>
    public static async Task<TeacherDetail> WithActorsAsync(TeacherDetail detail, IAuditActorDirectory actors, CancellationToken cancellationToken)
    {
        var ids = new[] { detail.CreatedByUserId, detail.LastModifiedByUserId }.OfType<string>().Distinct().ToList();
        var emails = await actors.GetEmailsAsync(ids, cancellationToken);
        return detail with
        {
            CreatedBy = detail.CreatedByUserId is { } c ? emails.GetValueOrDefault(c) : null,
            LastModifiedBy = detail.LastModifiedByUserId is { } m ? emails.GetValueOrDefault(m) : null,
        };
    }

    public static string RoleList(IEnumerable<string> roles) => string.Join(", ", PortalRoles.Normalize(roles));

    public static void Stage(
        IAuditEventRecorder recorder, AuditEventDefinition definition, TeacherProfile profile, IReadOnlyList<AuditFieldChange> changes,
        IReadOnlyDictionary<string, object?>? extra = null)
    {
        var metadata = new Dictionary<string, object?> { ["userId"] = profile.UserId };
        foreach (var (key, value) in extra ?? new Dictionary<string, object?>())
        {
            metadata[key] = value;
        }

        recorder.Stage(new AuditEventRequest(definition, AuditOutcome.Success)
        {
            EntityType = "Teacher",
            EntityId = profile.Id.ToString(),
            Metadata = metadata,
            Changes = changes,
        });
    }

    public static IReadOnlyList<AuditFieldChange> CreatedChanges(
        TeacherProfile profile, string email, TeacherAccountStatus status, string roles, string? defaultRole) =>
    [
        new("displayName", null, profile.DisplayName),
        new("email", null, email),
        new("employeeStaffId", null, profile.EmployeeStaffId),
        new("phone", null, profile.Phone),
        new("designationId", null, profile.DesignationId?.ToString()),
        new("isActive", null, profile.IsActive),
        new("accountStatus", null, status.ToString()),
        new("roles", null, roles),
        new("defaultRole", null, defaultRole),
    ];
}
