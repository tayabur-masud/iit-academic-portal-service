using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.UserManagement;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Staff;
using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Application.Staff;

/// <summary>
/// Staff Management: lists, reads, creates, and updates staff profiles. Creating takes an email. A new email gets one pending Admin
/// account (Admin role and default, no password) and its profile in one transaction with both audit events, and only after the commit is
/// the common credential-setup email requested; a request that cannot be kept leaves the account pending for a reissue in Accounts. An
/// email of an existing Admin account that is not deactivated and has no profile gets a profile attached, with the account untouched and
/// no setup email. Every create and update needs the saving Admin's confirmation of manual verification, recorded in the change's audit
/// event. An Admin's own profile follows the same rules. Roles and credentials are never changed here. Authorization is the caller's job.
/// </summary>
public sealed class StaffProfileService(
    IStaffStore store,
    IAuditEventRecorder recorder,
    IAuditActorDirectory actors,
    UserManager<PortalUser> userManager,
    INotificationRequestService notifications)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    private const string NotFoundMessage = "No staff profile has that identifier.";
    private const string DuplicateMessage = "This account already has a staff profile. Open it from the staff directory to change it.";

    public async Task<StaffResult<StaffListPage>> ListAsync(StaffQuery query, CancellationToken cancellationToken)
    {
        var errors = new StaffErrors();
        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"The page size must be between 1 and {MaxPageSize}.");
        }

        var page = query.Page ?? 1;
        if (page < 1)
        {
            errors.Add("page", "The page number must be 1 or more.");
        }

        if (errors.Any)
        {
            return StaffResult<StaffListPage>.Invalid(errors);
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : NameNormalizer.Normalize(query.Search);
        var result = await store.ListAsync(search, page, pageSize, cancellationToken);
        return new StaffResult<StaffListPage>(StaffOutcome.Ok, result with { Items = await WithActorsAsync(result.Items, cancellationToken) });
    }

    public async Task<StaffResult<StaffDetail>> GetAsync(Guid profileId, CancellationToken cancellationToken) =>
        await store.GetDetailAsync(profileId, cancellationToken) is { } detail
            ? new StaffResult<StaffDetail>(StaffOutcome.Ok, (await WithActorsAsync([detail], cancellationToken))[0])
            : StaffResult<StaffDetail>.NotFound(NotFoundMessage);

    public async Task<StaffResult<StaffDetail>> CreateAsync(StaffInput input, CancellationToken cancellationToken)
    {
        var errors = new StaffErrors();
        var email = (input.Email ?? string.Empty).Trim();
        if (UserProvisioningService.ValidateEmail(email) is { } emailProblem)
        {
            errors.Add("email", emailProblem == "Enter the email address of the account." ? "Enter the staff member's email address." : emailProblem);
        }

        await ValidateAsync(input, currentDesignationId: null, errors, cancellationToken);
        if (errors.Any)
        {
            return StaffResult<StaffDetail>.Invalid(errors);
        }

        return await userManager.FindByEmailAsync(email) is { } existing
            ? await AttachAsync(existing.Id, input, cancellationToken)
            : await ProvisionAsync(email, input, cancellationToken);
    }

    public async Task<StaffResult<StaffDetail>> UpdateAsync(Guid profileId, StaffInput input, CancellationToken cancellationToken)
    {
        if (await store.FindProfileAsync(profileId, cancellationToken) is not { } profile)
        {
            return StaffResult<StaffDetail>.NotFound(NotFoundMessage);
        }

        var errors = new StaffErrors();
        await ValidateAsync(input, profile.DesignationId, errors, cancellationToken);
        if (errors.Any)
        {
            return StaffResult<StaffDetail>.Invalid(errors);
        }

        // A deactivated staff member, or one whose account lost the Admin role or was deactivated in Accounts, stays as history but is
        // not changed.
        if (!profile.IsActive)
        {
            return StaffResult<StaffDetail>.Conflict("This staff member is deactivated, so the profile cannot be changed.");
        }

        if (await store.FindAccountAsync(profile.UserId, cancellationToken) is not { } account)
        {
            return StaffResult<StaffDetail>.NotFound("The portal account of this staff profile no longer exists.");
        }

        if (Ineligible(account) is { } refusal)
        {
            return StaffResult<StaffDetail>.Conflict(refusal);
        }

        var (name, mobile, designation) = (NameNormalizer.Display(input.Name), NameNormalizer.Display(input.MobileNumber), input.DesignationId!.Value);
        var changes = new List<AuditFieldChange>();
        Track(changes, "name", profile.Name, name);
        Track(changes, "mobileNumber", profile.MobileNumber, mobile);
        Track(changes, "designationId", profile.DesignationId.ToString(), designation.ToString());
        if (changes.Count == 0)
        {
            // Nothing changed, so there is nothing to save and nothing to audit.
            return await GetAsync(profile.Id, cancellationToken);
        }

        profile.Update(name, mobile, designation);
        Stage(StaffAuditEventDefinitions.Updated, profile, changes);
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await GetAsync(profile.Id, cancellationToken);
    }

    /// <summary>An existing account: only an Admin account that is not deactivated and has no profile yet. The account is not touched.</summary>
    private async Task<StaffResult<StaffDetail>> AttachAsync(string userId, StaffInput input, CancellationToken cancellationToken)
    {
        var account = (await store.FindAccountAsync(userId, cancellationToken))!;
        if (Ineligible(account) is { } refusal)
        {
            return StaffResult<StaffDetail>.Conflict(refusal);
        }

        if (await store.ProfileExistsForUserAsync(account.UserId, cancellationToken))
        {
            return StaffResult<StaffDetail>.Conflict(DuplicateMessage);
        }

        var profile = StaffProfile.Create(account.UserId, input.Name!, input.MobileNumber!, input.DesignationId!.Value);
        store.Add(profile);
        Stage(StaffAuditEventDefinitions.Created, profile, CreatedChanges(profile), accountCreated: false);
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await CreatedAsync(profile.Id, accountCreated: false, setupRequested: false, cancellationToken);
    }

    /// <summary>A new email: the pending Admin account, its profile, and both audit events in one transaction; then the setup email.</summary>
    private async Task<StaffResult<StaffDetail>> ProvisionAsync(string email, StaffInput input, CancellationToken cancellationToken)
    {
        var user = new PortalUser
        {
            UserName = email,
            Email = email,
            AccountStatus = TeacherAccountStatus.PendingCredentialSetup,
            DefaultRole = PortalRoles.Admin,
        };

        StaffProfile profile;
        await using (var transaction = await store.BeginTransactionAsync(cancellationToken))
        {
            // No password: the account has no way to authenticate until credential setup, and it is pending besides.
            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded)
            {
                return IdentityFailure(created);
            }

            var assigned = await userManager.AddToRoleAsync(user, PortalRoles.Admin);
            if (!assigned.Succeeded)
            {
                throw new InvalidOperationException("The Admin role could not be assigned: " + string.Join("; ", assigned.Errors.Select(e => e.Code)));
            }

            recorder.Stage(new AuditEventRequest(UserAccountAuditEventDefinitions.Created, AuditOutcome.Success)
            {
                EntityType = UserAccountAuditEventDefinitions.EntityType,
                EntityId = user.Id,
                Changes =
                [
                    new AuditFieldChange("email", null, email),
                    new AuditFieldChange("accountStatus", null, user.AccountStatus.ToString()),
                    new AuditFieldChange("roles", null, PortalRoles.Admin),
                    new AuditFieldChange("defaultRole", null, PortalRoles.Admin),
                ],
            });
            profile = StaffProfile.Create(user.Id, input.Name!, input.MobileNumber!, input.DesignationId!.Value);
            store.Add(profile);
            Stage(StaffAuditEventDefinitions.Created, profile, CreatedChanges(profile), accountCreated: true);
            if (await SaveAsync(cancellationToken) is { } conflict)
            {
                return conflict;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // Only a committed account asks for its setup email. The request never throws; when it cannot be kept, the account stays
        // pending and an Admin reissues the email from Accounts.
        var request = await notifications.RequestAsync(AccountCredentialSetupService.InitialRequest(user.Id), cancellationToken);
        var setupRequested = request.Status == NotificationRequestStatus.Accepted && request.Accepted + request.Duplicates > 0;
        return await CreatedAsync(profile.Id, accountCreated: true, setupRequested, cancellationToken);
    }

    /// <summary>Name, Mobile number, Designation, and the confirmation. A newly chosen Designation must be live and active.</summary>
    private async Task ValidateAsync(StaffInput input, Guid? currentDesignationId, StaffErrors errors, CancellationToken cancellationToken)
    {
        var name = NameNormalizer.Display(input.Name);
        if (name.Length == 0)
        {
            errors.Add("name", "Enter the staff member's name.");
        }
        else if (name.Length > StaffProfile.MaxNameLength)
        {
            errors.Add("name", $"The name can have at most {StaffProfile.MaxNameLength} characters.");
        }

        var mobile = NameNormalizer.Display(input.MobileNumber);
        if (mobile.Length == 0)
        {
            errors.Add("mobileNumber", "Enter the mobile number.");
        }
        else if (mobile.Length > StaffProfile.MaxMobileNumberLength)
        {
            errors.Add("mobileNumber", $"The mobile number can have at most {StaffProfile.MaxMobileNumberLength} characters.");
        }

        if (input.DesignationId is not { } designationId || designationId == Guid.Empty)
        {
            errors.Add("designationId", "Choose a designation.");
        }
        else if (designationId != currentDesignationId && !await store.DesignationAvailableAsync(designationId, cancellationToken))
        {
            errors.Add("designationId", "Choose an active designation.");
        }

        if (input.ManualVerificationConfirmed != true)
        {
            errors.Add("manualVerificationConfirmed", "Confirm that you manually verified this information against the institution's records.");
        }
    }

    internal static string? Ineligible(StaffAccount account) =>
        account.Status == TeacherAccountStatus.Deactivated
            ? "This account is deactivated, so its staff profile cannot be created or changed."
            : !account.IsAdmin
                ? "This account does not have the Admin role. Staff profiles are only for Admin accounts; roles are assigned in Accounts."
                : null;

    private static IReadOnlyList<AuditFieldChange> CreatedChanges(StaffProfile profile) =>
    [
        new("name", null, profile.Name),
        new("mobileNumber", null, profile.MobileNumber),
        new("designationId", null, profile.DesignationId.ToString()),
        new("isActive", null, profile.IsActive),
    ];

    private void Stage(AuditEventDefinition definition, StaffProfile profile, IReadOnlyList<AuditFieldChange> changes, bool? accountCreated = null)
    {
        var metadata = new Dictionary<string, object?> { ["userId"] = profile.UserId, ["manualVerificationConfirmed"] = true };
        if (accountCreated is { } created)
        {
            metadata["accountCreated"] = created;
        }

        recorder.Stage(new AuditEventRequest(definition, AuditOutcome.Success)
        {
            EntityType = "StaffProfile",
            EntityId = profile.Id.ToString(),
            Metadata = metadata,
            Changes = changes,
        });
    }

    private async Task<StaffResult<StaffDetail>?> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.SaveAsync(cancellationToken);
            return null;
        }
        catch (StaffConflictException)
        {
            return StaffResult<StaffDetail>.Conflict("This email or account was just given a staff profile by another change. Reload and try again.");
        }
    }

    private async Task<StaffResult<StaffDetail>> CreatedAsync(Guid profileId, bool accountCreated, bool setupRequested, CancellationToken cancellationToken)
    {
        var result = await GetAsync(profileId, cancellationToken);
        return result with
        {
            Outcome = StaffOutcome.Created,
            Value = result.Value! with { AccountCreated = accountCreated, SetupEmailRequested = setupRequested },
        };
    }

    private static StaffResult<StaffDetail> IdentityFailure(IdentityResult result)
    {
        var errors = new StaffErrors();
        foreach (var error in result.Errors)
        {
            if (error.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName))
            {
                return StaffResult<StaffDetail>.Conflict("An account with this email was just created by another change. Reload and try again.");
            }

            if (error.Code is nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName))
            {
                errors.Add("email", "Enter a valid email address.");
            }
            else
            {
                throw new InvalidOperationException("The account could not be created: " + error.Code);
            }
        }

        return StaffResult<StaffDetail>.Invalid(errors);
    }

    private static void Track(List<AuditFieldChange> changes, string field, string before, string after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add(new AuditFieldChange(field, before, after));
        }
    }

    /// <summary>Adds the current email of the accounts that created and last changed each profile, in one lookup.</summary>
    internal async Task<IReadOnlyList<StaffDetail>> WithActorsAsync(IReadOnlyList<StaffDetail> details, CancellationToken cancellationToken)
    {
        var ids = details.SelectMany(d => new[] { d.CreatedByUserId, d.LastModifiedByUserId }).OfType<string>().Distinct().ToArray();
        if (ids.Length == 0)
        {
            return details;
        }

        var emails = await actors.GetEmailsAsync(ids, cancellationToken);
        string? Email(string? id) => id is not null && emails.TryGetValue(id, out var email) ? email : null;
        return details.Select(d => d with { CreatedBy = Email(d.CreatedByUserId), LastModifiedBy = Email(d.LastModifiedByUserId) }).ToList();
    }
}
