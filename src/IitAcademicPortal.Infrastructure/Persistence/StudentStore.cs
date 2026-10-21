using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Students;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Students;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace IitAcademicPortal.Infrastructure.Persistence;

/// <summary>
/// Student profiles, admissions, and academic history over the request's <see cref="PortalDbContext"/>. Identity, the student rows,
/// and the staged audit events share this context, so one transaction around them makes a change atomic. Configuration lookups go
/// through the global query filter, so a deleted Degree, Batch, Hall, or Upazila is never found.
/// </summary>
public sealed class StudentStore(PortalDbContext db) : IStudentStore
{
    private const string UniqueViolation = "23505";

    public async Task<ITeacherTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new Transaction(await db.Database.BeginTransactionAsync(cancellationToken));

    public Task<StudentProfile?> FindProfileByUserAsync(string userId, CancellationToken cancellationToken) =>
        db.StudentProfiles.Include(p => p.AcademicHistory).FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public Task<StudentProfile?> FindProfileAsync(Guid profileId, CancellationToken cancellationToken) =>
        db.StudentProfiles.Include(p => p.AcademicHistory).FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken);

    public Task<StudentAdmissionRecord?> FindAdmissionAsync(Guid admissionId, CancellationToken cancellationToken) =>
        db.StudentAdmissions.FirstOrDefaultAsync(a => a.Id == admissionId, cancellationToken);

    public Task<bool> AdmissionExistsAsync(Guid profileId, Guid degreeId, Guid batchId, Guid exceptAdmissionId, CancellationToken cancellationToken) =>
        db.StudentAdmissions.AnyAsync(
            a => a.StudentProfileId == profileId && a.DegreeId == degreeId && a.BatchId == batchId && a.Id != exceptAdmissionId, cancellationToken);

    public Task<bool> RegistrationNumberExistsAsync(string normalized, Guid exceptAdmissionId, CancellationToken cancellationToken) =>
        db.StudentAdmissions.AnyAsync(a => a.NormalizedRegistrationNumber == normalized && a.Id != exceptAdmissionId, cancellationToken);

    public Task<StudentReference?> FindDegreeAsync(Guid degreeId, CancellationToken cancellationToken) =>
        db.Degrees.AsNoTracking().Where(d => d.Id == degreeId)
            .Select(d => new StudentReference(d.Id, d.Name, d.IsActive, null, d.CreditsToBeCompleted, null, d.MinimumCgpaForCompletion))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<StudentReference?> FindBatchAsync(Guid batchId, CancellationToken cancellationToken) =>
        db.Batches.AsNoTracking().Where(b => b.Id == batchId)
            .Select(b => new StudentReference(b.Id, b.Name, b.IsActive, b.DegreeId, null, null))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<StudentReference?> FindHallAsync(Guid hallId, CancellationToken cancellationToken) =>
        db.Halls.AsNoTracking().Where(h => h.Id == hallId)
            .Select(h => new StudentReference(h.Id, h.Name, h.IsActive, null, null, null))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<StudentReference?> FindUpazilaAsync(Guid upazilaId, CancellationToken cancellationToken) =>
        db.Upazilas.AsNoTracking().Where(u => u.Id == upazilaId)
            .Select(u => new StudentReference(u.Id, u.Name, u.IsActive, u.DistrictId, null, null))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<StudentReference?> FindCityCorporationAsync(Guid cityCorporationId, CancellationToken cancellationToken) =>
        db.CityCorporations.AsNoTracking().Where(c => c.Id == cityCorporationId)
            .Select(c => new StudentReference(c.Id, c.Name, c.IsActive, c.DistrictId, null, null))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<StudentReference?> FindInstitutionAsync(Guid institutionId, CancellationToken cancellationToken) =>
        db.EducationalInstitutions.AsNoTracking().Where(i => i.Id == institutionId)
            .Select(i => new StudentReference(i.Id, i.Name, i.IsActive, null, null, i.Type))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<StudentReference?> FindBoardAsync(Guid boardId, CancellationToken cancellationToken) =>
        db.EducationBoards.AsNoTracking().Where(b => b.Id == boardId)
            .Select(b => new StudentReference(b.Id, b.Name, b.IsActive, null, null, null))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<StudentReference?> FindGroupAsync(Guid groupId, CancellationToken cancellationToken) =>
        db.EducationGroups.AsNoTracking().Where(g => g.Id == groupId)
            .Select(g => new StudentReference(g.Id, g.Name, g.IsActive, null, null, null))
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(StudentProfile profile) => db.StudentProfiles.Add(profile);

    public void Add(StudentAdmissionRecord admission) => db.StudentAdmissions.Add(admission);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (Classify(ex) is { } kind)
        {
            throw new StudentConflictException(kind, ex);
        }
    }

    public async Task<StudentAdmissionDetail?> GetDetailAsync(Guid admissionId, CancellationToken cancellationToken)
    {
        var admission = await db.StudentAdmissions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == admissionId, cancellationToken);
        if (admission is null)
        {
            return null;
        }

        var profile = await ProfileDetailAsync(db.StudentProfiles.AsNoTracking().Where(p => p.Id == admission.StudentProfileId), cancellationToken);
        var names = await db.StudentAdmissions.AsNoTracking().Where(a => a.Id == admissionId).Select(a => new
        {
            Degree = db.Degrees.Where(d => d.Id == a.DegreeId).Select(d => d.Name).FirstOrDefault(),
            Batch = db.Batches.Where(b => b.Id == a.BatchId).Select(b => b.Name).FirstOrDefault(),
            Hall = db.Halls.Where(h => h.Id == a.AssignedHallId).Select(h => h.Name).FirstOrDefault(),
        }).SingleAsync(cancellationToken);

        return new StudentAdmissionDetail(
            admission.Id, profile!, admission.DegreeId, names.Degree, admission.BatchId, names.Batch, admission.AdmissionSerialNumber,
            admission.AdmissionExamRoll, admission.ClassRoll, admission.AssignedHallId, names.Hall, admission.RegistrationNumber,
            admission.CreatedAtUtc, admission.CreatedByUserId, null, admission.LastModifiedAtUtc, admission.LastModifiedByUserId, null);
    }

    public Task<StudentProfileDetail?> GetProfileOfUserAsync(string userId, CancellationToken cancellationToken) =>
        ProfileDetailAsync(db.StudentProfiles.AsNoTracking().Where(p => p.UserId == userId), cancellationToken);

    public async Task<StudentListPage> ListAsync(StudentListFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = from admission in db.StudentAdmissions.AsNoTracking()
                    join profile in db.StudentProfiles.AsNoTracking() on admission.StudentProfileId equals profile.Id
                    join user in db.Users.AsNoTracking() on profile.UserId equals user.Id
                    select new { admission, profile, user };

        if (filter.DegreeId is { } degreeId)
        {
            query = query.Where(r => r.admission.DegreeId == degreeId);
        }

        if (filter.BatchId is { } batchId)
        {
            query = query.Where(r => r.admission.BatchId == batchId);
        }

        if (filter.Search is { } search)
        {
            var upper = search.ToUpperInvariant();
            query = query.Where(r =>
                r.profile.ApplicantName.ToUpper().Contains(upper)
                || (r.user.NormalizedEmail != null && r.user.NormalizedEmail.Contains(upper))
                || r.profile.ApplicantMobile.Contains(search)
                || r.admission.AdmissionExamRoll.ToUpper().Contains(upper)
                || (r.admission.ClassRoll != null && r.admission.ClassRoll.ToUpper().Contains(upper))
                || (r.admission.NormalizedRegistrationNumber != null && r.admission.NormalizedRegistrationNumber.Contains(upper)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var items = await query
            .OrderBy(r => r.profile.ApplicantName.ToUpper()).ThenBy(r => r.admission.Id)
            .Skip(skip).Take(pageSize)
            .Select(r => new StudentAdmissionListItem(
                r.admission.Id,
                r.profile.Id,
                r.user.Id,
                r.profile.ApplicantName,
                r.user.Email ?? string.Empty,
                r.user.AccountStatus,
                r.admission.DegreeId,
                db.Degrees.Where(d => d.Id == r.admission.DegreeId).Select(d => d.Name).FirstOrDefault(),
                r.admission.BatchId,
                db.Batches.Where(b => b.Id == r.admission.BatchId).Select(b => b.Name).FirstOrDefault(),
                r.admission.AdmissionExamRoll,
                r.admission.ClassRoll,
                db.Halls.Where(h => h.Id == r.admission.AssignedHallId).Select(h => h.Name).FirstOrDefault(),
                r.admission.RegistrationNumber))
            .ToListAsync(cancellationToken);
        var totalPages = (int)Math.Min((total + pageSize - 1) / pageSize, int.MaxValue);
        return new StudentListPage(items, page, total, totalPages);
    }

    private async Task<StudentProfileDetail?> ProfileDetailAsync(IQueryable<StudentProfile> profiles, CancellationToken cancellationToken)
    {
        var profile = await profiles.Include(p => p.AcademicHistory).FirstOrDefaultAsync(cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var account = await db.Users.AsNoTracking().Where(u => u.Id == profile.UserId)
            .Select(u => new { u.Email, u.AccountStatus }).SingleAsync(cancellationToken);
        var admissions = await db.StudentAdmissions.AsNoTracking().Where(a => a.StudentProfileId == profile.Id)
            .OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id)
            .Select(a => new StudentAdmissionSummary(
                a.Id,
                a.DegreeId,
                db.Degrees.Where(d => d.Id == a.DegreeId).Select(d => d.Name).FirstOrDefault(),
                a.BatchId,
                db.Batches.Where(b => b.Id == a.BatchId).Select(b => b.Name).FirstOrDefault(),
                a.RegistrationNumber,
                a.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var history = profile.AcademicHistory.OrderBy(h => h.SortOrder).ThenBy(h => h.Id).Select(h => new AcademicHistoryEntry(
            h.Id, h.Category, h.Examination, h.SchoolOrCollege, h.Board, h.Group, h.Department, h.University, h.Subject, h.Institution,
            h.Year, h.MarksObtained, h.MarksPercentage, h.Cgpa, h.Class, h.Division,
            h.SchoolOrCollegeId, h.UniversityId, h.InstitutionId, h.BoardId, h.GroupId)).ToList();

        return new StudentProfileDetail(
            profile.Id, profile.UserId, account.Email ?? string.Empty, account.AccountStatus, profile.ApplicantName, profile.FatherName,
            profile.MotherName, profile.DateOfBirth, profile.PlaceOfBirth, profile.MaritalStatus, profile.Nationality, profile.ApplicantMobile,
            await AddressAsync(profile.MailingAddressDetails, profile.MailingCellNumber, profile.MailingUpazilaId, profile.MailingCityCorporationId, cancellationToken),
            await AddressAsync(profile.PermanentAddressDetails, profile.PermanentCellNumber, profile.PermanentUpazilaId, profile.PermanentCityCorporationId, cancellationToken),
            profile.GuardianName, profile.GuardianRelationship, profile.GuardianAddress, profile.GuardianCellNumber, history, admissions);
    }

    // The District and Division always come from the locality (an Upazila or a City Corporation), so the path can never disagree with it.
    // The filters are lifted for the locality so an address keeps its path even if it were deleted (deleting one in use is refused anyway).
    private async Task<StudentAddress?> AddressAsync(
        string? details, string? cell, Guid? upazilaId, Guid? cityCorporationId, CancellationToken cancellationToken)
    {
        if (details is null && cell is null && upazilaId is null && cityCorporationId is null)
        {
            return null;
        }

        if (upazilaId is { } upazila)
        {
            var path = await (from u in db.Upazilas.IgnoreQueryFilters().AsNoTracking()
                              join district in db.Districts.IgnoreQueryFilters().AsNoTracking() on u.DistrictId equals district.Id
                              join division in db.Divisions.IgnoreQueryFilters().AsNoTracking() on district.DivisionId equals division.Id
                              where u.Id == upazila
                              select new StudentAddress(details, cell, u.Id, u.Name, null, null, district.Id, district.Name, division.Id, division.Name))
                .FirstOrDefaultAsync(cancellationToken);
            return path ?? new StudentAddress(details, cell, upazila, null, null, null, null, null, null, null);
        }

        if (cityCorporationId is { } city)
        {
            var path = await (from c in db.CityCorporations.IgnoreQueryFilters().AsNoTracking()
                              join district in db.Districts.IgnoreQueryFilters().AsNoTracking() on c.DistrictId equals district.Id
                              join division in db.Divisions.IgnoreQueryFilters().AsNoTracking() on district.DivisionId equals division.Id
                              where c.Id == city
                              select new StudentAddress(details, cell, null, null, c.Id, c.Name, district.Id, district.Name, division.Id, division.Name))
                .FirstOrDefaultAsync(cancellationToken);
            return path ?? new StudentAddress(details, cell, null, null, city, null, null, null, null, null);
        }

        return new StudentAddress(details, cell, null, null, null, null, null, null, null, null);
    }

    /// <summary>Recognises a unique violation from PostgreSQL, or from SQLite in the test host, and which rule it was.</summary>
    private static StudentConflictKind? Classify(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException postgres)
            {
                return postgres.SqlState == UniqueViolation ? Kind(postgres.ConstraintName ?? postgres.MessageText) : null;
            }

            // The SQLite provider is referenced only by the tests, so it is recognised by name.
            if (inner.GetType().Name == "SqliteException")
            {
                return inner.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ? Kind(inner.Message) : null;
            }
        }

        return null;
    }

    private static StudentConflictKind Kind(string text) =>
        text.Contains("NormalizedRegistrationNumber", StringComparison.Ordinal) ? StudentConflictKind.RegistrationNumber
        : text.Contains("StudentProfileId_DegreeId_BatchId", StringComparison.Ordinal)
            || (text.Contains("StudentAdmissions.StudentProfileId", StringComparison.Ordinal) && text.Contains("BatchId", StringComparison.Ordinal))
            ? StudentConflictKind.DuplicateAdmission
        : text.Contains("StudentProfiles", StringComparison.Ordinal) && text.Contains("UserId", StringComparison.Ordinal) ? StudentConflictKind.Profile
        : text.Contains("EmailIndex", StringComparison.Ordinal) || text.Contains("NormalizedEmail", StringComparison.Ordinal)
            || text.Contains("UserNameIndex", StringComparison.Ordinal) || text.Contains("NormalizedUserName", StringComparison.Ordinal)
            ? StudentConflictKind.Email
            : StudentConflictKind.Other;

    private sealed class Transaction(IDbContextTransaction inner) : ITeacherTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => inner.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
