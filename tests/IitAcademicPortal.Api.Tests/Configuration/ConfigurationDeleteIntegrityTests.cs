using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Configuration;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// Deleting a configuration record that something still refers to is refused by PostgreSQL and reported as a
/// conflict, with no cascade and no orphan; deleting an unreferenced record works. Set AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class ConfigurationDeleteIntegrityTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private PostgresTestDatabase Db => fixture.Database!;

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private static T Named<T>(string name, T record) where T : ConfigurationRecord
    {
        record.Rename(name);
        return record;
    }

    private async Task SaveAsync(params ConfigurationRecord[] records)
    {
        await using var db = Db.CreateRuntimeContext();
        db.AddRange(records);
        await db.SaveChangesAsync();
    }

    private async Task<ConfigurationConflictKind?> DeleteAsync<T>(Guid id) where T : ConfigurationRecord
    {
        await using var db = Db.CreateRuntimeContext();
        var store = new ConfigurationStore(db);
        var record = await store.FindAsync<T>(id, CancellationToken.None);
        Assert.NotNull(record);
        store.Remove(record);
        try
        {
            await store.SaveAsync(CancellationToken.None);
            return null;
        }
        catch (ConfigurationConflictException ex)
        {
            return ex.Kind;
        }
    }

    private async Task<bool> ExistsAsync<T>(Guid id) where T : ConfigurationRecord
    {
        await using var db = Db.CreateRuntimeContext();
        return await db.Set<T>().AnyAsync(e => e.Id == id);
    }

    [PostgresFact]
    public async Task A_referenced_degree_semester_and_session_cannot_be_deleted_and_stay_intact()
    {
        var (degree, semester, session) = (Named(Unique("Deg"), new Degree()), Named(Unique("Sem"), new Semester()), Named(Unique("Ses"), new AcademicSession()));
        await SaveAsync(degree, semester, session);
        var batch = Named(Unique("Batch"), new Batch { DegreeId = degree.Id, StartingSemesterId = semester.Id, AcademicSessionId = session.Id });
        await SaveAsync(batch);

        Assert.Equal(ConfigurationConflictKind.Referenced, await DeleteAsync<Degree>(degree.Id));
        Assert.Equal(ConfigurationConflictKind.Referenced, await DeleteAsync<Semester>(semester.Id));
        Assert.Equal(ConfigurationConflictKind.Referenced, await DeleteAsync<AcademicSession>(session.Id));

        Assert.True(await ExistsAsync<Degree>(degree.Id));
        Assert.True(await ExistsAsync<Semester>(semester.Id));
        Assert.True(await ExistsAsync<AcademicSession>(session.Id));
        Assert.True(await ExistsAsync<Batch>(batch.Id));
    }

    [PostgresFact]
    public async Task A_referenced_division_and_district_cannot_be_deleted_and_nothing_is_orphaned()
    {
        var division = Named(Unique("Div"), new Division());
        await SaveAsync(division);
        var district = Named(Unique("Dist"), new District { DivisionId = division.Id });
        await SaveAsync(district);
        var upazila = Named(Unique("Upa"), new Upazila { DistrictId = district.Id });
        await SaveAsync(upazila);

        Assert.Equal(ConfigurationConflictKind.Referenced, await DeleteAsync<Division>(division.Id));
        Assert.Equal(ConfigurationConflictKind.Referenced, await DeleteAsync<District>(district.Id));

        Assert.True(await ExistsAsync<Division>(division.Id));
        Assert.True(await ExistsAsync<District>(district.Id));
        Assert.True(await ExistsAsync<Upazila>(upazila.Id));
    }

    [PostgresFact]
    public async Task Once_the_dependents_are_gone_the_parents_can_be_deleted_bottom_up()
    {
        var division = Named(Unique("Div"), new Division());
        await SaveAsync(division);
        var district = Named(Unique("Dist"), new District { DivisionId = division.Id });
        await SaveAsync(district);
        var upazila = Named(Unique("Upa"), new Upazila { DistrictId = district.Id });
        await SaveAsync(upazila);

        Assert.Null(await DeleteAsync<Upazila>(upazila.Id));
        Assert.Null(await DeleteAsync<District>(district.Id));
        Assert.Null(await DeleteAsync<Division>(division.Id));
        Assert.False(await ExistsAsync<Division>(division.Id));
    }

    [PostgresFact]
    public async Task Unreferenced_records_of_every_catalog_can_be_deleted()
    {
        ConfigurationRecord[] records =
        [
            Named(Unique("Inst"), new EducationalInstitution { Type = EducationalInstitutionType.School }),
            Named(Unique("Board"), new EducationBoard()), Named(Unique("Group"), new EducationGroup()),
            Named(Unique("Exam"), new ExaminationType()), Named(Unique("Deg"), new Degree()),
            Named(Unique("Sem"), new Semester()), Named(Unique("Ses"), new AcademicSession()), Named(Unique("Des"), new Designation()),
        ];
        await SaveAsync(records);

        Assert.Null(await DeleteAsync<EducationalInstitution>(records[0].Id));
        Assert.Null(await DeleteAsync<EducationBoard>(records[1].Id));
        Assert.Null(await DeleteAsync<EducationGroup>(records[2].Id));
        Assert.Null(await DeleteAsync<ExaminationType>(records[3].Id));
        Assert.Null(await DeleteAsync<Degree>(records[4].Id));
        Assert.Null(await DeleteAsync<Semester>(records[5].Id));
        Assert.Null(await DeleteAsync<AcademicSession>(records[6].Id));
        Assert.Null(await DeleteAsync<Designation>(records[7].Id));
    }
}
