using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Teachers;
using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Application.Teachers;

/// <summary>
/// Adds a teacher: one portal account (pending, with the Teacher role as its default) and one linked profile, in a single
/// transaction with the audit event. No password is set and no email is sent; the account cannot sign in until credential setup
/// (a later feature) activates it. Authorization is the caller's job (the Admin policy on the controllers).
/// </summary>
public sealed class TeacherProvisioningService(
    UserManager<PortalUser> userManager,
    ITeacherStore store,
    IAuditEventRecorder recorder,
    IAuditActorDirectory actors)
{
    public async Task<TeacherResult<TeacherDetail>> CreateAsync(TeacherInput input, CancellationToken cancellationToken)
    {
        var errors = new TeacherErrors();
        TeacherSupport.Validate(input, errors);
        var email = (input.Email ?? string.Empty).Trim();
        if (!errors.Has("email") && await userManager.FindByEmailAsync(email) is not null)
        {
            errors.Add("email", "An account with this email already exists.");
        }

        await TeacherSupport.ValidateReferencesAsync(input, Guid.Empty, null, store, errors, cancellationToken);
        if (errors.Any)
        {
            return TeacherResult<TeacherDetail>.Invalid(errors);
        }

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        var user = new PortalUser
        {
            UserName = email,
            Email = email,
            AccountStatus = TeacherAccountStatus.PendingCredentialSetup,
            DefaultRole = PortalRoles.Teacher,
        };

        // No password: the account has no way to authenticate until credential setup, and it is pending besides.
        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            return IdentityFailure(created);
        }

        var assigned = await userManager.AddToRoleAsync(user, PortalRoles.Teacher);
        if (!assigned.Succeeded)
        {
            throw new InvalidOperationException("The Teacher role could not be assigned: " + string.Join("; ", assigned.Errors.Select(e => e.Code)));
        }

        var profile = TeacherProfile.Create(user.Id);
        profile.Update(input.DisplayName!, input.EmployeeStaffId, input.Phone, input.DesignationId);
        store.Add(profile);
        TeacherSupport.Stage(recorder, TeacherAuditEventDefinitions.Created, profile,
            TeacherSupport.CreatedChanges(profile, email, user.AccountStatus, PortalRoles.Teacher, user.DefaultRole));

        try
        {
            await store.SaveAsync(cancellationToken);
        }
        catch (TeacherConflictException conflict)
        {
            return RaceFailure(conflict);
        }

        await transaction.CommitAsync(cancellationToken);
        var detail = await store.GetDetailAsync(profile.Id, cancellationToken);
        return new TeacherResult<TeacherDetail>(TeacherOutcome.Created, await TeacherSupport.WithActorsAsync(detail!, actors, cancellationToken));
    }

    private static TeacherResult<TeacherDetail> IdentityFailure(IdentityResult result)
    {
        var errors = new TeacherErrors();
        foreach (var error in result.Errors)
        {
            if (error.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName))
            {
                errors.Add("email", "An account with this email already exists.");
            }
            else if (error.Code is nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName))
            {
                errors.Add("email", "Enter a valid email address.");
            }
            else
            {
                throw new InvalidOperationException("The account could not be created: " + error.Code);
            }
        }

        return TeacherResult<TeacherDetail>.Invalid(errors);
    }

    // A duplicate that raced past the checks above and was caught by the database's unique indexes.
    internal static TeacherResult<TeacherDetail> RaceFailure(TeacherConflictException conflict)
    {
        var errors = new TeacherErrors();
        switch (conflict.Kind)
        {
            case TeacherConflictKind.StaffId:
                errors.Add("employeeStaffId", "Another teacher already has this staff ID.");
                break;
            case TeacherConflictKind.Email:
                errors.Add("email", "An account with this email already exists.");
                break;
            default:
                return TeacherResult<TeacherDetail>.Conflict("The change conflicts with another change. Reload and try again.");
        }

        return TeacherResult<TeacherDetail>.Invalid(errors);
    }
}
