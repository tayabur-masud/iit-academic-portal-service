using System.ComponentModel.DataAnnotations;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.UserManagement;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Students;
using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Application.Students;

/// <summary>
/// Student admissions. One Student profile per person, linked to one Student account (the account keeps the email); one admission per
/// Degree and Batch. The first admission for an email creates the pending, passwordless Student account, its profile, and the admission
/// in one transaction with their audit events, and requests the common setup email only after the commit. A later admission for the
/// same email reuses the account and profile without touching the account or requesting setup again. An email that belongs to any
/// other kind of account is refused; accounts are never merged. Authorization is the caller's job.
/// </summary>
public sealed class StudentAdmissionService(
    UserManager<PortalUser> userManager,
    IStudentStore store,
    IAuditEventRecorder audit,
    IAuditActorDirectory actors,
    INotificationRequestService notifications,
    TimeProvider clock)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int MaxEmailLength = 256;
    public const int MaxHistoryEntries = 20;

    public const string NotStudentAccountMessage =
        "This email belongs to a portal account that is not a Student account. Use a different email; accounts are never merged.";

    public const string DuplicateAdmissionMessage = "This student already has an admission for the selected degree and batch.";

    public const string DuplicateRegistrationMessage = "Another admission already has this registration number.";

    // The catalog records chosen in the history being saved, found during validation and used for the name snapshots. The service is
    // scoped to one request and handles one write per request.
    private readonly Dictionary<Guid, StudentReference> catalog = [];

    public async Task<StudentResult<StudentListPage>> ListAsync(StudentQuery query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"The page size must be between 1 and {MaxPageSize}."];
        }

        var page = query.Page ?? 1;
        if (page < 1)
        {
            errors["page"] = ["The page number must be 1 or more."];
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (search?.Length > MaxEmailLength)
        {
            errors["search"] = [$"The search can have at most {MaxEmailLength} characters."];
        }

        if (errors.Count > 0)
        {
            return StudentResult<StudentListPage>.Invalid(errors);
        }

        var result = await store.ListAsync(new StudentListFilter(search, query.DegreeId, query.BatchId), page, pageSize, cancellationToken);
        return new StudentResult<StudentListPage>(StudentOutcome.Ok, result);
    }

    public async Task<StudentResult<StudentAdmissionDetail>> GetAsync(Guid admissionId, CancellationToken cancellationToken) =>
        await DetailAsync(admissionId, cancellationToken) is { } detail
            ? new StudentResult<StudentAdmissionDetail>(StudentOutcome.Ok, detail)
            : StudentResult<StudentAdmissionDetail>.NotFound;

    /// <summary>What an admission for this email would do, and the existing profile to prefill when it is a returning Student.</summary>
    public async Task<StudentResult<StudentLookup>> LookupAsync(string? email, CancellationToken cancellationToken)
    {
        var normalized = (email ?? string.Empty).Trim();
        if (ValidateEmail(normalized) is { } error)
        {
            return StudentResult<StudentLookup>.Invalid(new Dictionary<string, string[]> { ["email"] = [error] });
        }

        var user = await userManager.FindByEmailAsync(normalized);
        if (user is null)
        {
            return new StudentResult<StudentLookup>(StudentOutcome.Ok, new StudentLookup(StudentLookupStatus.New, null));
        }

        if (!await IsStudentAccountAsync(user))
        {
            return new StudentResult<StudentLookup>(StudentOutcome.Ok, new StudentLookup(StudentLookupStatus.NotStudentAccount, null));
        }

        var profile = await store.GetProfileOfUserAsync(user.Id, cancellationToken);
        return new StudentResult<StudentLookup>(StudentOutcome.Ok, new StudentLookup(StudentLookupStatus.ExistingStudent, profile));
    }

    public async Task<StudentResult<StudentAdmissionCreated>> CreateAsync(StudentAdmissionInput input, CancellationToken cancellationToken)
    {
        var email = (input.Email ?? string.Empty).Trim();
        if (ValidateEmail(email) is { } emailError)
        {
            var errors = new StudentErrors();
            errors.Add("email", emailError);
            await ValidateAsync(input, null, null, errors, cancellationToken);
            return StudentResult<StudentAdmissionCreated>.Invalid(errors.ToDictionary());
        }

        // The account decides everything else: a new one, a reused Student account (with or without a profile), or a refusal.
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null && !await IsStudentAccountAsync(user))
        {
            return StudentResult<StudentAdmissionCreated>.Conflict(NotStudentAccountMessage);
        }

        var profile = user is null ? null : await store.FindProfileByUserAsync(user.Id, cancellationToken);
        var validation = new StudentErrors();
        await ValidateAsync(input, profile, null, validation, cancellationToken);
        if (validation.Any)
        {
            return StudentResult<StudentAdmissionCreated>.Invalid(validation.ToDictionary());
        }

        if (profile is not null && await store.AdmissionExistsAsync(profile.Id, input.DegreeId!.Value, input.BatchId!.Value, Guid.Empty, cancellationToken))
        {
            return StudentResult<StudentAdmissionCreated>.Conflict(DuplicateAdmissionMessage);
        }

        var registration = RegistrationNumber(input.RegistrationNumber);
        if (registration.Normalized is { } normalizedRegistration
            && await store.RegistrationNumberExistsAsync(normalizedRegistration, Guid.Empty, cancellationToken))
        {
            return StudentResult<StudentAdmissionCreated>.Conflict(DuplicateRegistrationMessage);
        }

        var accountCreated = user is null;
        var profileCreated = profile is null;
        StudentAdmissionRecord admission;
        await using (var transaction = await store.BeginTransactionAsync(cancellationToken))
        {
            if (user is null)
            {
                user = new PortalUser
                {
                    UserName = email,
                    Email = email,
                    AccountStatus = TeacherAccountStatus.PendingCredentialSetup,
                    DefaultRole = PortalRoles.Student,
                };

                // No password: the account cannot authenticate until its holder completes credential setup.
                var created = await userManager.CreateAsync(user);
                if (!created.Succeeded)
                {
                    return IdentityFailure<StudentAdmissionCreated>(created);
                }

                Require(await userManager.AddToRoleAsync(user, PortalRoles.Student));
                audit.Stage(new AuditEventRequest(UserAccountAuditEventDefinitions.Created, AuditOutcome.Success)
                {
                    EntityType = UserAccountAuditEventDefinitions.EntityType,
                    EntityId = user.Id,
                    Changes =
                    [
                        new AuditFieldChange("email", null, email),
                        new AuditFieldChange("accountStatus", null, user.AccountStatus.ToString()),
                        new AuditFieldChange("roles", null, PortalRoles.Student),
                        new AuditFieldChange("defaultRole", null, PortalRoles.Student),
                    ],
                });
            }

            if (profile is null)
            {
                profile = StudentProfile.Create(user.Id);
                store.Add(profile);
            }

            var profileChanges = ApplyProfile(profile, input);
            if (profileCreated || profileChanges.Count > 0)
            {
                StageProfile(profileCreated ? StudentAuditEventDefinitions.ProfileCreated : StudentAuditEventDefinitions.ProfileUpdated, profile, profileChanges);
            }

            admission = StudentAdmissionRecord.Create(profile.Id);
            var admissionChanges = ApplyAdmission(admission, input, registration, isNew: true);
            store.Add(admission);
            StageAdmission(StudentAuditEventDefinitions.AdmissionCreated, admission, profile, admissionChanges,
                new Dictionary<string, object?> { ["accountCreated"] = accountCreated, ["profileCreated"] = profileCreated });

            if (await SaveAsync<StudentAdmissionCreated>(cancellationToken) is { } conflict)
            {
                return conflict;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // Only a committed new account asks for its setup email. The request never throws, and a reused account never asks again.
        var setupRequested = false;
        if (accountCreated)
        {
            var request = await notifications.RequestAsync(AccountCredentialSetupService.InitialRequest(user.Id), cancellationToken);
            setupRequested = request.Status == NotificationRequestStatus.Accepted;
        }

        var detail = await DetailAsync(admission.Id, cancellationToken);
        return new StudentResult<StudentAdmissionCreated>(
            StudentOutcome.Created, new StudentAdmissionCreated(detail!, accountCreated, profileCreated, setupRequested));
    }

    /// <summary>
    /// Replaces the shared profile values, the academic history, and this admission's fields. Hall and registration number are this
    /// admission's own and can each be set or cleared without the other. The account (email, status, credentials) never changes here.
    /// </summary>
    public async Task<StudentResult<StudentAdmissionDetail>> UpdateAsync(Guid admissionId, StudentAdmissionInput input, CancellationToken cancellationToken)
    {
        var admission = await store.FindAdmissionAsync(admissionId, cancellationToken);
        if (admission is null || await store.FindProfileAsync(admission.StudentProfileId, cancellationToken) is not { } profile)
        {
            return StudentResult<StudentAdmissionDetail>.NotFound;
        }

        var errors = new StudentErrors();
        await ValidateAsync(input, profile, admission, errors, cancellationToken);
        if (errors.Any)
        {
            return StudentResult<StudentAdmissionDetail>.Invalid(errors.ToDictionary());
        }

        if (await store.AdmissionExistsAsync(profile.Id, input.DegreeId!.Value, input.BatchId!.Value, admission.Id, cancellationToken))
        {
            return StudentResult<StudentAdmissionDetail>.Conflict(DuplicateAdmissionMessage);
        }

        var registration = RegistrationNumber(input.RegistrationNumber);
        if (registration.Normalized is { } normalizedRegistration
            && await store.RegistrationNumberExistsAsync(normalizedRegistration, admission.Id, cancellationToken))
        {
            return StudentResult<StudentAdmissionDetail>.Conflict(DuplicateRegistrationMessage);
        }

        await using (var transaction = await store.BeginTransactionAsync(cancellationToken))
        {
            var profileChanges = ApplyProfile(profile, input);
            var admissionChanges = ApplyAdmission(admission, input, registration, isNew: false);
            if (profileChanges.Count == 0 && admissionChanges.Count == 0)
            {
                return await GetAsync(admission.Id, cancellationToken);
            }

            if (profileChanges.Count > 0)
            {
                StageProfile(StudentAuditEventDefinitions.ProfileUpdated, profile, profileChanges);
            }

            if (admissionChanges.Count > 0)
            {
                StageAdmission(StudentAuditEventDefinitions.AdmissionUpdated, admission, profile, admissionChanges);
            }

            if (await SaveAsync<StudentAdmissionDetail>(cancellationToken) is { } conflict)
            {
                return conflict;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return await GetAsync(admission.Id, cancellationToken);
    }

    private async Task ValidateAsync(
        StudentAdmissionInput input, StudentProfile? profile, StudentAdmissionRecord? admission, StudentErrors errors, CancellationToken cancellationToken)
    {
        errors.Required("applicantName", input.ApplicantName, StudentProfile.MaxTextLength, "Enter the applicant's name.");
        errors.Required("applicantMobile", input.ApplicantMobile, StudentProfile.MaxPhoneLength, "Enter the applicant's mobile number.");
        errors.Optional("fatherName", input.FatherName, StudentProfile.MaxTextLength);
        errors.Optional("motherName", input.MotherName, StudentProfile.MaxTextLength);
        errors.Optional("placeOfBirth", input.PlaceOfBirth, StudentProfile.MaxTextLength);
        errors.Optional("maritalStatus", input.MaritalStatus, StudentProfile.MaxTextLength);
        errors.Optional("nationality", input.Nationality, StudentProfile.MaxTextLength);
        errors.Optional("guardianName", input.GuardianName, StudentProfile.MaxTextLength);
        errors.Optional("guardianRelationship", input.GuardianRelationship, StudentProfile.MaxTextLength);
        errors.Optional("guardianAddress", input.GuardianAddress, StudentProfile.MaxAddressLength);
        errors.Optional("guardianCellNumber", input.GuardianCellNumber, StudentProfile.MaxPhoneLength);
        if (input.DateOfBirth is { } born && born > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
        {
            errors.Add("dateOfBirth", "The date of birth cannot be in the future.");
        }

        await ValidateAddressAsync("mailingAddress", input.MailingAddress, profile?.MailingUpazilaId, errors, cancellationToken);
        await ValidateAddressAsync("permanentAddress", input.PermanentAddress, profile?.PermanentUpazilaId, errors, cancellationToken);

        errors.Required("admissionSerialNumber", input.AdmissionSerialNumber, StudentAdmissionRecord.MaxIdentifierLength, "Enter the admission test serial number.");
        errors.Required("admissionExamRoll", input.AdmissionExamRoll, StudentAdmissionRecord.MaxIdentifierLength, "Enter the admission test roll number (Exam Roll).");
        errors.Optional("classRoll", input.ClassRoll, StudentAdmissionRecord.MaxIdentifierLength);
        errors.Optional("registrationNumber", input.RegistrationNumber, StudentAdmissionRecord.MaxIdentifierLength);

        // A reference that is already on this admission may stay even if it was deactivated since; a newly chosen one must be active.
        StudentReference? degree = null;
        if (input.DegreeId is not { } degreeId)
        {
            errors.Add("degreeId", "Select a degree.");
        }
        else if ((degree = await store.FindDegreeAsync(degreeId, cancellationToken)) is null
            || (!degree.IsActive && degreeId != admission?.DegreeId))
        {
            errors.Add("degreeId", "Select an active degree.");
        }

        if (input.BatchId is not { } batchId)
        {
            errors.Add("batchId", "Select a batch.");
        }
        else if (await store.FindBatchAsync(batchId, cancellationToken) is not { } batch || (!batch.IsActive && batchId != admission?.BatchId))
        {
            errors.Add("batchId", "Select an active batch.");
        }
        else if (degree is not null && batch.ParentId != degree.Id)
        {
            errors.Add("batchId", "The batch does not belong to the selected degree.");
        }

        if (input.AssignedHallId is { } hallId
            && (await store.FindHallAsync(hallId, cancellationToken) is not { } hall || (!hall.IsActive && hallId != admission?.AssignedHallId)))
        {
            errors.Add("assignedHallId", "Select an active hall.");
        }

        await ValidateHistoryAsync(input.AcademicHistory, profile, errors, cancellationToken);
    }

    private async Task ValidateAddressAsync(string key, StudentAddressInput? address, Guid? current, StudentErrors errors, CancellationToken cancellationToken)
    {
        if (address is null || (Clean(address.Details) is null && address.UpazilaId is null && Clean(address.CellNumber) is null))
        {
            return;
        }

        errors.Required($"{key}.details", address.Details, StudentProfile.MaxAddressLength, "Enter the address details (house, road, area).");
        errors.Optional($"{key}.cellNumber", address.CellNumber, StudentProfile.MaxPhoneLength);
        if (address.UpazilaId is not { } upazilaId)
        {
            errors.Add($"{key}.upazilaId", "Select the division, district, and upazila.");
        }
        else if (await store.FindUpazilaAsync(upazilaId, cancellationToken) is not { } upazila || (!upazila.IsActive && upazilaId != current))
        {
            errors.Add($"{key}.upazilaId", "Select an active upazila.");
        }
    }

    private async Task ValidateHistoryAsync(
        IReadOnlyList<AcademicHistoryInput>? history, StudentProfile? profile, StudentErrors errors, CancellationToken cancellationToken)
    {
        if (history is null)
        {
            return;
        }

        if (history.Count > MaxHistoryEntries)
        {
            errors.Add("academicHistory", $"Enter at most {MaxHistoryEntries} academic-history entries.");
            return;
        }

        var known = profile?.AcademicHistory.Select(h => h.Id).ToHashSet() ?? [];
        var seen = new HashSet<Guid>();
        for (var i = 0; i < history.Count; i++)
        {
            var entry = history[i];
            var key = $"academicHistory[{i}]";
            if (entry is null)
            {
                errors.Add(key, "Remove the empty academic-history entry.");
                continue;
            }

            if (ParseCategory(entry.Category) is null)
            {
                errors.Add($"{key}.category", "Select SSC/O-Level, HSC/A-Level, Bachelor/Graduation, Master, PGDIT, or Other.");
            }

            if (entry.Id is { } id && (!known.Contains(id) || !seen.Add(id)))
            {
                errors.Add($"{key}.id", "This academic-history entry does not belong to the student. Reload and try again.");
            }

            foreach (var (field, value) in HistoryValues(entry))
            {
                errors.Optional($"{key}.{field}", value, StudentAcademicHistory.MaxValueLength);
            }

            var row = entry.Id is { } rowId ? profile?.AcademicHistory.FirstOrDefault(h => h.Id == rowId) : null;
            await ValidateChoiceAsync($"{key}.schoolOrCollegeId", entry.SchoolOrCollegeId, row?.SchoolOrCollegeId, CatalogKind.SchoolOrCollege, errors, cancellationToken);
            await ValidateChoiceAsync($"{key}.universityId", entry.UniversityId, row?.UniversityId, CatalogKind.University, errors, cancellationToken);
            await ValidateChoiceAsync($"{key}.institutionId", entry.InstitutionId, row?.InstitutionId, CatalogKind.Institution, errors, cancellationToken);
            await ValidateChoiceAsync($"{key}.boardId", entry.BoardId, row?.BoardId, CatalogKind.Board, errors, cancellationToken);
            await ValidateChoiceAsync($"{key}.groupId", entry.GroupId, row?.GroupId, CatalogKind.Group, errors, cancellationToken);
        }
    }

    private enum CatalogKind
    {
        SchoolOrCollege,
        University,
        Institution,
        Board,
        Group,
    }

    /// <summary>
    /// A catalog choice must exist; a newly chosen one must be active (the one a row already has may stay after it is deactivated), a
    /// school or college must be a School or College, and a university a University. Found records are kept for the name snapshot.
    /// </summary>
    private async Task ValidateChoiceAsync(
        string key, Guid? id, Guid? current, CatalogKind kind, StudentErrors errors, CancellationToken cancellationToken)
    {
        if (id is not { } chosen)
        {
            return;
        }

        var record = kind switch
        {
            CatalogKind.Board => await store.FindBoardAsync(chosen, cancellationToken),
            CatalogKind.Group => await store.FindGroupAsync(chosen, cancellationToken),
            _ => await store.FindInstitutionAsync(chosen, cancellationToken),
        };
        var noun = kind switch
        {
            CatalogKind.SchoolOrCollege => "school or college",
            CatalogKind.University => "university",
            CatalogKind.Institution => "institution",
            CatalogKind.Board => "board",
            _ => "group",
        };
        if (record is null || (!record.IsActive && chosen != current))
        {
            errors.Add(key, $"Select an active {noun}, or type the name if it is not listed.");
            return;
        }

        var typeMatches = kind switch
        {
            CatalogKind.SchoolOrCollege => record.InstitutionType is EducationalInstitutionType.School or EducationalInstitutionType.College,
            CatalogKind.University => record.InstitutionType is EducationalInstitutionType.University,
            _ => true,
        };
        if (!typeMatches)
        {
            errors.Add(key, $"Select a {noun}.");
            return;
        }

        catalog[chosen] = record;
    }

    internal static AcademicHistoryCategory? ParseCategory(string? category) =>
        Enum.TryParse<AcademicHistoryCategory>(category?.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            && !int.TryParse(category, out _)
            ? parsed
            : null;

    private static IEnumerable<(string Field, string? Value)> HistoryValues(AcademicHistoryInput entry) =>
    [
        ("examination", entry.Examination), ("schoolOrCollege", entry.SchoolOrCollege), ("board", entry.Board), ("group", entry.Group),
        ("department", entry.Department), ("university", entry.University), ("subject", entry.Subject), ("institution", entry.Institution),
        ("year", entry.Year), ("marksObtained", entry.MarksObtained), ("marksPercentage", entry.MarksPercentage), ("cgpa", entry.Cgpa),
        ("class", entry.Class), ("division", entry.Division),
    ];

    /// <summary>Copies the shared profile values and replaces the history. Returns the indicator fields that changed.</summary>
    private List<AuditFieldChange> ApplyProfile(StudentProfile profile, StudentAdmissionInput input)
    {
        var changes = new List<AuditFieldChange>();
        void Set(string field, string? current, string? next, Action<string?> assign)
        {
            if (!string.Equals(current, next, StringComparison.Ordinal))
            {
                changes.Add(new AuditFieldChange(field, current, next));
                assign(next);
            }
        }

        Set("applicantName", profile.ApplicantName, Clean(input.ApplicantName), v => profile.ApplicantName = v ?? string.Empty);
        Set("fatherName", profile.FatherName, Clean(input.FatherName), v => profile.FatherName = v);
        Set("motherName", profile.MotherName, Clean(input.MotherName), v => profile.MotherName = v);
        Set("dateOfBirth", profile.DateOfBirth?.ToString("O"), input.DateOfBirth?.ToString("O"), _ => profile.DateOfBirth = input.DateOfBirth);
        Set("placeOfBirth", profile.PlaceOfBirth, Clean(input.PlaceOfBirth), v => profile.PlaceOfBirth = v);
        Set("maritalStatus", profile.MaritalStatus, Clean(input.MaritalStatus), v => profile.MaritalStatus = v);
        Set("nationality", profile.Nationality, Clean(input.Nationality), v => profile.Nationality = v);
        Set("applicantMobile", profile.ApplicantMobile, Clean(input.ApplicantMobile), v => profile.ApplicantMobile = v ?? string.Empty);

        var mailing = AddressParts(input.MailingAddress);
        Set("mailingAddress", AddressKey(profile.MailingAddressDetails, profile.MailingUpazilaId, profile.MailingCellNumber), AddressKey(mailing), _ =>
        {
            (profile.MailingAddressDetails, profile.MailingUpazilaId, profile.MailingCellNumber) = mailing;
        });
        var permanent = AddressParts(input.PermanentAddress);
        Set("permanentAddress", AddressKey(profile.PermanentAddressDetails, profile.PermanentUpazilaId, profile.PermanentCellNumber), AddressKey(permanent), _ =>
        {
            (profile.PermanentAddressDetails, profile.PermanentUpazilaId, profile.PermanentCellNumber) = permanent;
        });

        var guardianBefore = string.Join('|', profile.GuardianName, profile.GuardianRelationship, profile.GuardianAddress, profile.GuardianCellNumber);
        var guardianAfter = string.Join('|', Clean(input.GuardianName), Clean(input.GuardianRelationship), Clean(input.GuardianAddress), Clean(input.GuardianCellNumber));
        Set("guardian", guardianBefore, guardianAfter, _ =>
        {
            profile.GuardianName = Clean(input.GuardianName);
            profile.GuardianRelationship = Clean(input.GuardianRelationship);
            profile.GuardianAddress = Clean(input.GuardianAddress);
            profile.GuardianCellNumber = Clean(input.GuardianCellNumber);
        });

        if (ReplaceHistory(profile, input.AcademicHistory ?? []))
        {
            changes.Add(new AuditFieldChange("academicHistory", "before", "after"));
        }

        return changes;
    }

    /// <summary>
    /// Makes the profile's history equal the submitted list: kept rows updated in place, new rows added, the rest removed. A newly chosen
    /// catalog record sets its name as the snapshot; a choice the row already had keeps the snapshot it was saved with, so renaming the
    /// record later never rewrites history; without a choice the typed name is kept.
    /// </summary>
    private bool ReplaceHistory(StudentProfile profile, IReadOnlyList<AcademicHistoryInput> entries)
    {
        var before = HistorySignature(profile.AcademicHistory.OrderBy(h => h.SortOrder));
        var kept = new HashSet<Guid>();
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var row = entry.Id is { } id ? profile.AcademicHistory.Single(h => h.Id == id) : null;
            if (row is null)
            {
                row = StudentAcademicHistory.Create(profile.Id);
                profile.AcademicHistory.Add(row);
            }

            kept.Add(row.Id);
            row.SortOrder = i;
            row.Category = ParseCategory(entry.Category)!.Value;
            row.Examination = Clean(entry.Examination);
            (row.SchoolOrCollegeId, row.SchoolOrCollege) = Choice(entry.SchoolOrCollegeId, entry.SchoolOrCollege, row.SchoolOrCollegeId, row.SchoolOrCollege);
            (row.BoardId, row.Board) = Choice(entry.BoardId, entry.Board, row.BoardId, row.Board);
            (row.GroupId, row.Group) = Choice(entry.GroupId, entry.Group, row.GroupId, row.Group);
            row.Department = Clean(entry.Department);
            (row.UniversityId, row.University) = Choice(entry.UniversityId, entry.University, row.UniversityId, row.University);
            row.Subject = Clean(entry.Subject);
            (row.InstitutionId, row.Institution) = Choice(entry.InstitutionId, entry.Institution, row.InstitutionId, row.Institution);
            row.Year = Clean(entry.Year);
            row.MarksObtained = Clean(entry.MarksObtained);
            row.MarksPercentage = Clean(entry.MarksPercentage);
            row.Cgpa = Clean(entry.Cgpa);
            row.Class = Clean(entry.Class);
            row.Division = Clean(entry.Division);
        }

        profile.AcademicHistory.RemoveAll(h => !kept.Contains(h.Id));
        return before != HistorySignature(profile.AcademicHistory.OrderBy(h => h.SortOrder));
    }

    private (Guid? Id, string? Name) Choice(Guid? chosen, string? typed, Guid? currentId, string? currentName) =>
        chosen is { } id
            ? (id, id == currentId && currentName is not null ? currentName : catalog[id].Name)
            : (null, Clean(typed));

    private static string HistorySignature(IEnumerable<StudentAcademicHistory> rows) => string.Join('\n', rows.Select(h => string.Join('|',
        h.Id, h.Category, h.Examination, h.SchoolOrCollege, h.SchoolOrCollegeId, h.Board, h.BoardId, h.Group, h.GroupId, h.Department,
        h.University, h.UniversityId, h.Subject, h.Institution, h.InstitutionId, h.Year, h.MarksObtained, h.MarksPercentage, h.Cgpa, h.Class,
        h.Division)));

    private static List<AuditFieldChange> ApplyAdmission(
        StudentAdmissionRecord admission, StudentAdmissionInput input, (string? Display, string? Normalized) registration, bool isNew)
    {
        var changes = new List<AuditFieldChange>();
        void Track(string field, object? current, object? next)
        {
            if (isNew || !Equals(current, next))
            {
                changes.Add(new AuditFieldChange(field, isNew ? null : current, next));
            }
        }

        Track("degreeId", admission.DegreeId, input.DegreeId!.Value);
        Track("batchId", admission.BatchId, input.BatchId!.Value);
        Track("admissionSerialNumber", admission.AdmissionSerialNumber, Clean(input.AdmissionSerialNumber));
        Track("admissionExamRoll", admission.AdmissionExamRoll, Clean(input.AdmissionExamRoll));
        Track("classRoll", admission.ClassRoll, Clean(input.ClassRoll));
        Track("assignedHallId", admission.AssignedHallId, input.AssignedHallId);
        Track("registrationNumber", admission.RegistrationNumber, registration.Display);

        admission.DegreeId = input.DegreeId!.Value;
        admission.BatchId = input.BatchId!.Value;
        admission.AdmissionSerialNumber = Clean(input.AdmissionSerialNumber)!;
        admission.AdmissionExamRoll = Clean(input.AdmissionExamRoll)!;
        admission.ClassRoll = Clean(input.ClassRoll);
        admission.AssignedHallId = input.AssignedHallId;
        admission.SetRegistrationNumber(registration.Display, registration.Normalized);
        return changes.Where(c => isNew ? c.NewValue is not null : true).ToList();
    }

    private void StageProfile(AuditEventDefinition definition, StudentProfile profile, IReadOnlyList<AuditFieldChange> changes) =>
        audit.Stage(new AuditEventRequest(definition, AuditOutcome.Success)
        {
            EntityType = StudentAuditEventDefinitions.ProfileEntity,
            EntityId = profile.Id.ToString(),
            Metadata = new Dictionary<string, object?> { ["userId"] = profile.UserId },
            Changes = changes,
        });

    private void StageAdmission(
        AuditEventDefinition definition, StudentAdmissionRecord admission, StudentProfile profile, IReadOnlyList<AuditFieldChange> changes,
        IReadOnlyDictionary<string, object?>? extra = null)
    {
        var metadata = new Dictionary<string, object?> { ["studentProfileId"] = profile.Id.ToString(), ["userId"] = profile.UserId };
        foreach (var (key, value) in extra ?? new Dictionary<string, object?>())
        {
            metadata[key] = value;
        }

        audit.Stage(new AuditEventRequest(definition, AuditOutcome.Success)
        {
            EntityType = StudentAuditEventDefinitions.AdmissionEntity,
            EntityId = admission.Id.ToString(),
            Metadata = metadata,
            Changes = changes,
        });
    }

    private async Task<StudentResult<T>?> SaveAsync<T>(CancellationToken cancellationToken)
    {
        try
        {
            await store.SaveAsync(cancellationToken);
            return null;
        }
        catch (StudentConflictException conflict)
        {
            return StudentResult<T>.Conflict(conflict.Kind switch
            {
                StudentConflictKind.RegistrationNumber => DuplicateRegistrationMessage,
                StudentConflictKind.DuplicateAdmission => DuplicateAdmissionMessage,
                StudentConflictKind.Email => UserProvisioningService.DuplicateEmailMessage,
                _ => "The admission conflicts with another change. Reload and try again.",
            });
        }
    }

    private async Task<bool> IsStudentAccountAsync(PortalUser user)
    {
        var roles = PortalRoles.Normalize(await userManager.GetRolesAsync(user));
        return roles.Count == 1 && roles[0] == PortalRoles.Student;
    }

    private async Task<StudentAdmissionDetail?> DetailAsync(Guid admissionId, CancellationToken cancellationToken)
    {
        var detail = await store.GetDetailAsync(admissionId, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var ids = new[] { detail.CreatedByUserId, detail.LastModifiedByUserId }.OfType<string>().Distinct().ToList();
        var emails = ids.Count == 0 ? new Dictionary<string, string>() : await actors.GetEmailsAsync(ids, cancellationToken);
        return detail with
        {
            CreatedBy = detail.CreatedByUserId is { } c ? emails.GetValueOrDefault(c) : null,
            LastModifiedBy = detail.LastModifiedByUserId is { } m ? emails.GetValueOrDefault(m) : null,
        };
    }

    private static StudentResult<T> IdentityFailure<T>(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            if (error.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName))
            {
                return StudentResult<T>.Conflict(UserProvisioningService.DuplicateEmailMessage);
            }

            if (error.Code is nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName))
            {
                return StudentResult<T>.Invalid(new Dictionary<string, string[]> { ["email"] = ["Enter a valid email address."] });
            }
        }

        throw new InvalidOperationException("The Student account could not be created: " + string.Join("; ", result.Errors.Select(e => e.Code)));
    }

    private static void Require(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("The Student role could not be assigned: " + string.Join("; ", result.Errors.Select(e => e.Code)));
        }
    }

    private static string? ValidateEmail(string email) =>
        email.Length == 0 ? "Enter the applicant's email address."
        : email.Length > MaxEmailLength || !new EmailAddressAttribute().IsValid(email) ? "Enter a valid email address."
        : null;

    private static (string? Display, string? Normalized) RegistrationNumber(string? value) =>
        (NameNormalizer.DisplayCode(value), NameNormalizer.NormalizeCode(value));

    private static (string? Details, Guid? UpazilaId, string? Cell) AddressParts(StudentAddressInput? address) =>
        address is null ? (null, null, null) : (Clean(address.Details), address.UpazilaId, Clean(address.CellNumber));

    private static string AddressKey((string? Details, Guid? UpazilaId, string? Cell) address) =>
        AddressKey(address.Details, address.UpazilaId, address.Cell);

    private static string AddressKey(string? details, Guid? upazilaId, string? cell) => string.Join('|', details, upazilaId, cell);

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Field-keyed validation messages, collected before anything is written.</summary>
internal sealed class StudentErrors
{
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    public bool Any => errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!errors.TryGetValue(field, out var list))
        {
            errors[field] = list = [];
        }

        if (!list.Contains(message))
        {
            list.Add(message);
        }
    }

    public void Required(string field, string? value, int maxLength, string missing)
    {
        var clean = StudentAdmissionService.Clean(value);
        if (clean is null)
        {
            Add(field, missing);
        }
        else if (clean.Length > maxLength)
        {
            Add(field, $"Use at most {maxLength} characters.");
        }
    }

    public void Optional(string field, string? value, int maxLength)
    {
        if (StudentAdmissionService.Clean(value)?.Length > maxLength)
        {
            Add(field, $"Use at most {maxLength} characters.");
        }
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() => errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
}
