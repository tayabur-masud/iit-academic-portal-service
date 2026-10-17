using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Application.Teachers;

/// <summary>
/// Reads one teacher and updates the profile fields and the account email. The account keeps its identifier, so sessions and
/// history stay linked. A request that sets the profile inactive deactivates the teacher in the same transaction.
/// </summary>
public sealed class TeacherProfileService(
    UserManager<PortalUser> userManager,
    ITeacherStore store,
    TeacherLifecycleService lifecycle,
    IAuditEventRecorder recorder,
    IAuditActorDirectory actors)
{
    public async Task<TeacherResult<TeacherDetail>> GetAsync(Guid teacherId, CancellationToken cancellationToken) =>
        await store.GetDetailAsync(teacherId, cancellationToken) is { } detail
            ? new TeacherResult<TeacherDetail>(TeacherOutcome.Ok, await TeacherSupport.WithActorsAsync(detail, actors, cancellationToken))
            : TeacherResult<TeacherDetail>.NotFound;

    public async Task<TeacherResult<TeacherDetail>> UpdateAsync(Guid teacherId, TeacherInput input, CancellationToken cancellationToken)
    {
        var profile = await store.FindProfileAsync(teacherId, cancellationToken);
        if (profile is null || await userManager.FindByIdAsync(profile.UserId) is not { } user)
        {
            return TeacherResult<TeacherDetail>.NotFound;
        }

        var errors = new TeacherErrors();
        TeacherSupport.Validate(input, errors);
        var email = (input.Email ?? string.Empty).Trim();
        var emailChanged = !errors.Has("email")
            && !string.Equals(userManager.NormalizeEmail(email), user.NormalizedEmail, StringComparison.Ordinal);
        if (emailChanged && await userManager.FindByEmailAsync(email) is { } other && other.Id != user.Id)
        {
            errors.Add("email", "An account with this email already exists.");
        }

        await TeacherSupport.ValidateReferencesAsync(input, profile.Id, profile.DesignationId, store, errors, cancellationToken);
        var deactivating = input.IsActive == false && profile.IsActive;
        if (input.IsActive == true && !profile.IsActive)
        {
            errors.Add("isActive", "A deactivated teacher cannot be reactivated.");
        }

        if (errors.Any)
        {
            return TeacherResult<TeacherDetail>.Invalid(errors);
        }

        var changes = new List<AuditFieldChange>();
        var newName = NameNormalizer.Display(input.DisplayName);
        var newStaffId = NameNormalizer.DisplayCode(input.EmployeeStaffId);
        var newPhone = NameNormalizer.DisplayCode(input.Phone);
        Track(changes, "displayName", profile.DisplayName, newName);
        Track(changes, "employeeStaffId", profile.EmployeeStaffId, newStaffId);
        Track(changes, "phone", profile.Phone, newPhone);
        Track(changes, "designationId", profile.DesignationId?.ToString(), input.DesignationId?.ToString());
        if (emailChanged)
        {
            changes.Add(new AuditFieldChange("email", user.Email, email));
        }

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        profile.Update(newName, newStaffId, newPhone, input.DesignationId);
        if (emailChanged)
        {
            // Email is the sign-in name here, so the two change together.
            var renamed = await userManager.SetEmailAsync(user, email);
            if (renamed.Succeeded)
            {
                renamed = await userManager.SetUserNameAsync(user, email);
            }

            if (!renamed.Succeeded)
            {
                return EmailFailure(renamed);
            }
        }

        if (changes.Count > 0)
        {
            TeacherSupport.Stage(recorder, TeacherAuditEventDefinitions.Updated, profile, changes);
        }

        if (deactivating && await lifecycle.ApplyAsync(profile, user, cancellationToken) is { } refused)
        {
            return refused;
        }

        try
        {
            await store.SaveAsync(cancellationToken);
        }
        catch (TeacherConflictException conflict)
        {
            return TeacherProvisioningService.RaceFailure(conflict);
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(teacherId, cancellationToken);
    }

    private static void Track(List<AuditFieldChange> changes, string field, string? before, string? after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add(new AuditFieldChange(field, before, after));
        }
    }

    private static TeacherResult<TeacherDetail> EmailFailure(IdentityResult result)
    {
        var errors = new TeacherErrors();
        foreach (var error in result.Errors)
        {
            errors.Add("email", error.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)
                ? "An account with this email already exists."
                : "Enter a valid email address.");
        }

        return TeacherResult<TeacherDetail>.Invalid(errors);
    }
}
