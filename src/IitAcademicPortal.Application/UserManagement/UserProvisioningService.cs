using System.ComponentModel.DataAnnotations;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Application.UserManagement;

/// <summary>
/// Provisions a generic Admin or Student account: one Identity user with that single role as its default, pending credential setup
/// and with no password, committed in one transaction with its audit event. After the commit the common setup email is requested;
/// that request never fails the action, and a failed delivery leaves the account pending. Teacher accounts belong to Teacher
/// Management and Coordinator accounts to the batch-coordinator assignment owner, so both are refused here. Authorization is the
/// caller's job.
/// </summary>
public sealed class UserProvisioningService(
    UserManager<PortalUser> userManager,
    ITeacherStore unitOfWork,
    IUserAccountStore accounts,
    IAuditActorDirectory actors,
    IAuditEventRecorder audit,
    INotificationRequestService notifications)
{
    public const int MaxEmailLength = 256;

    public const string DuplicateEmailMessage = "An account with this email already exists.";

    /// <summary>The account types this feature provisions directly.</summary>
    public static IReadOnlyList<string> AccountTypes { get; } = [PortalRoles.Admin, PortalRoles.Student];

    public async Task<AccountResult<UserAccountDetail>> CreateAsync(CreateAccountInput input, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var email = (input.Email ?? string.Empty).Trim();
        if (ValidateEmail(email) is { } emailError)
        {
            errors["email"] = [emailError];
        }

        var accountType = (input.AccountType ?? string.Empty).Trim();
        var role = PortalRoles.All.FirstOrDefault(r => string.Equals(r, accountType, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            errors["accountType"] = ["Choose Admin or Student."];
        }

        if (errors.Count > 0)
        {
            return AccountResult<UserAccountDetail>.Invalid(errors);
        }

        if (role == PortalRoles.Teacher)
        {
            return AccountResult<UserAccountDetail>.Conflict("Teacher accounts are added through Teacher Management, which also creates the teacher profile.");
        }

        if (role == PortalRoles.Coordinator)
        {
            return AccountResult<UserAccountDetail>.Conflict("Coordinator access comes from a batch-coordinator assignment and cannot be created here.");
        }

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return AccountResult<UserAccountDetail>.Conflict(DuplicateEmailMessage);
        }

        var user = new PortalUser
        {
            UserName = email,
            Email = email,
            AccountStatus = TeacherAccountStatus.PendingCredentialSetup,
            DefaultRole = role,
        };

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            // No password: the account has no way to authenticate until credential setup, and it is pending besides.
            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
            {
                return IdentityFailure(created);
            }

            Require(await userManager.AddToRoleAsync(user, role!));
            audit.Stage(new AuditEventRequest(UserAccountAuditEventDefinitions.Created, AuditOutcome.Success)
            {
                EntityType = UserAccountAuditEventDefinitions.EntityType,
                EntityId = user.Id,
                Changes =
                [
                    new AuditFieldChange("email", null, email),
                    new AuditFieldChange("accountStatus", null, user.AccountStatus.ToString()),
                    new AuditFieldChange("roles", null, role),
                    new AuditFieldChange("defaultRole", null, role),
                ],
            });

            try
            {
                await unitOfWork.SaveAsync(cancellationToken);
            }
            catch (TeacherConflictException conflict) when (conflict.Kind == TeacherConflictKind.Email)
            {
                return AccountResult<UserAccountDetail>.Conflict(DuplicateEmailMessage);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // Only a committed account asks for its setup email. The request is durable and never throws; a rolled-back creation returns
        // above, so it requests nothing.
        await notifications.RequestAsync(AccountCredentialSetupService.InitialRequest(user.Id), cancellationToken);

        var detail = await UserDirectoryService.GetDetailAsync(accounts, actors, user.Id, cancellationToken);
        return new AccountResult<UserAccountDetail>(AccountOutcome.Created, detail);
    }

    internal static string? ValidateEmail(string email) =>
        email.Length == 0 ? "Enter the email address of the account."
        : email.Length > MaxEmailLength || !new EmailAddressAttribute().IsValid(email) ? "Enter a valid email address."
        : null;

    internal static AccountResult<UserAccountDetail> IdentityFailure(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            if (error.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName))
            {
                return AccountResult<UserAccountDetail>.Conflict(DuplicateEmailMessage);
            }

            if (error.Code is nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName))
            {
                return AccountResult<UserAccountDetail>.Invalid("email", "Enter a valid email address.");
            }

            if (error.Code is nameof(IdentityErrorDescriber.ConcurrencyFailure))
            {
                return AccountResult<UserAccountDetail>.Conflict(UserAccessService.StaleMessage);
            }
        }

        throw new InvalidOperationException("The account change failed: " + string.Join("; ", result.Errors.Select(e => e.Code)));
    }

    internal static void Require(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("An account change failed: " + string.Join("; ", result.Errors.Select(e => e.Code)));
        }
    }
}
