using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Configuration;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Infrastructure.Persistence;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// The Configuration migration, indexes, and foreign keys on a real PostgreSQL 18 database, used as the restricted
/// runtime role a deployment uses (so the default privileges for the new tables are proven too). Set
/// AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class ConfigurationPostgresTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private PostgresTestDatabase Db => fixture.Database!;

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private static T Named<T>(string name, T record) where T : ConfigurationRecord
    {
        record.Rename(name);
        if (record is Degree degree)
        {
            degree.SetCreditsToBeCompleted(120m);
        }
        return record;
    }

    /// <summary>Saves the records in a fresh unit of work as the runtime role.</summary>
    private async Task SaveAsync(params ConfigurationRecord[] records)
    {
        await using var db = Db.CreateRuntimeContext();
        db.AddRange(records);
        await new ConfigurationStore(db).SaveAsync(CancellationToken.None);
    }

    private async Task<ConfigurationConflictKind> ConflictAsync(params ConfigurationRecord[] records)
    {
        await using var db = Db.CreateRuntimeContext();
        db.AddRange(records);
        var error = await Assert.ThrowsAsync<ConfigurationConflictException>(() => new ConfigurationStore(db).SaveAsync(CancellationToken.None));
        return error.Kind;
    }

    [PostgresFact]
    public async Task The_runtime_role_can_use_every_configuration_table_without_a_further_grant()
    {
        var name = Unique("Grant check");
        await SaveAsync(
            Named(name, new EducationalInstitution { Type = EducationalInstitutionType.School }),
            Named(name, new EducationBoard()), Named(name, new EducationGroup()), Named(name, new ExaminationType()),
            Named(name, new Degree()), Named(name, new Semester()), Named(name, new AcademicSession()), Named(name, new Designation()), Named(name, new Hall()),
            Named(name, new Division()));
    }

    [PostgresFact]
    public async Task Every_independent_catalog_has_a_globally_unique_normalized_name()
    {
        var name = Unique("Same Name");
        Func<ConfigurationRecord>[] makers =
        [
            () => Named(name, new EducationalInstitution { Type = EducationalInstitutionType.College }),
            () => Named(name, new EducationBoard()),
            () => Named(name, new EducationGroup()),
            () => Named(name, new ExaminationType()),
            () => Named(name, new Degree()),
            () => Named(name, new Semester()),
            () => Named(name, new AcademicSession()),
            () => Named(name, new Designation()),
            () => Named(name, new Hall()),
            () => Named(name, new Division()),
        ];

        foreach (var make in makers)
        {
            await SaveAsync(make());
            // Same display text with different case and spacing normalizes to the same value.
            var twin = make();
            twin.Rename($"  {name.ToLowerInvariant().Replace(" ", "   ")} ");
            Assert.Equal(ConfigurationConflictKind.Duplicate, await ConflictAsync(twin));
        }
    }

    [PostgresFact]
    public async Task The_same_name_in_different_catalogs_is_allowed()
    {
        var name = Unique("Shared");

        await SaveAsync(Named(name, new Degree()), Named(name, new Semester()), Named(name, new EducationGroup()));
    }

    [PostgresFact]
    public async Task Optional_codes_are_unique_when_present_and_never_collide_when_absent()
    {
        var code = Unique("code").ToUpperInvariant().Replace(" ", "-");
        Func<string, string?, ConfigurationRecord>[] makers =
        [
            (n, c) => CodedNamed(n, c, new EducationalInstitution { Type = EducationalInstitutionType.University }),
            (n, c) => CodedNamed(n, c, new EducationBoard()),
            (n, c) => CodedNamed(n, c, new Degree()),
            (n, c) => CodedNamed(n, c, new Designation()),
            (n, c) => CodedNamed(n, c, new Hall()),
        ];

        foreach (var make in makers)
        {
            // Any number of records without a code.
            await SaveAsync(make(Unique("No code"), null), make(Unique("Blank code"), "   "), make(Unique("No code either"), null));

            await SaveAsync(make(Unique("With code"), code));
            Assert.Equal(ConfigurationConflictKind.Duplicate, await ConflictAsync(make(Unique("Other"), code.ToLowerInvariant())));
        }

        // The same code in two different catalogs is fine.
        var shared = Unique("share").Replace(" ", "-");
        await SaveAsync(CodedNamed(Unique("A"), shared, new Degree()), CodedNamed(Unique("B"), shared, new Designation()));
    }

    private static T CodedNamed<T>(string name, string? code, T record) where T : ConfigurationRecord, ICodedRecord
    {
        Named(name, record);
        record.SetCode(code);
        return record;
    }

    [PostgresFact]
    public async Task A_batch_name_is_unique_within_its_degree_but_not_across_degrees_or_sessions()
    {
        var (semester, session, otherSession) = (Named(Unique("Sem"), new Semester()), Named(Unique("Ses"), new AcademicSession()), Named(Unique("Ses2"), new AcademicSession()));
        var (degree, otherDegree) = (Named(Unique("Deg"), new Degree()), Named(Unique("Deg2"), new Degree()));
        await SaveAsync(semester, session, otherSession, degree, otherDegree);
        Batch Make(string name, Degree d, AcademicSession s) =>
            Named(name, new Batch { DegreeId = d.Id, StartingSemesterId = semester.Id, AcademicSessionId = s.Id });

        var name = Unique("Batch");
        await SaveAsync(Make(name, degree, session));

        // Same Degree: rejected whatever the Session.
        Assert.Equal(ConfigurationConflictKind.Duplicate, await ConflictAsync(Make(name.ToLowerInvariant(), degree, otherSession)));
        // Another Degree, same Session or different: allowed.
        await SaveAsync(Make(name, otherDegree, session), Make(Unique("Other batch"), degree, session), Make(Unique("Another"), degree, session));
    }

    [PostgresFact]
    public async Task Geographic_names_are_unique_within_their_parent_only()
    {
        var (a, b) = (Named(Unique("Div A"), new Division()), Named(Unique("Div B"), new Division()));
        await SaveAsync(a, b);
        var name = Unique("Central");
        var district = Named(name, new District { DivisionId = a.Id });
        await SaveAsync(district);

        Assert.Equal(ConfigurationConflictKind.Duplicate, await ConflictAsync(Named(name.ToUpperInvariant(), new District { DivisionId = a.Id })));
        var elsewhere = Named(name, new District { DivisionId = b.Id });
        await SaveAsync(elsewhere);

        var upazila = Unique("Sadar");
        await SaveAsync(Named(upazila, new Upazila { DistrictId = district.Id }));
        Assert.Equal(ConfigurationConflictKind.Duplicate, await ConflictAsync(Named(upazila, new Upazila { DistrictId = district.Id })));
        await SaveAsync(Named(upazila, new Upazila { DistrictId = elsewhere.Id }));
    }

    [PostgresFact]
    public async Task The_configuration_tables_have_unique_indexes_where_the_data_model_requires_them_and_no_cascading_keys()
    {
        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);

        async Task<List<string>> ColumnsAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var list = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(reader.GetString(0));
            }

            return list;
        }

        var unique = await ColumnsAsync(
            "SELECT tablename || ':' || indexdef FROM pg_indexes WHERE schemaname = 'public' AND indexdef LIKE 'CREATE UNIQUE INDEX%' " +
            "AND tablename IN ('Batches','Districts','Upazilas')");
        Assert.Contains(unique, u => u.StartsWith("Batches:") && u.Contains("(\"DegreeId\", \"NormalizedName\")"));
        Assert.Contains(unique, u => u.StartsWith("Districts:") && u.Contains("(\"DivisionId\", \"NormalizedName\")"));
        Assert.Contains(unique, u => u.StartsWith("Upazilas:") && u.Contains("(\"DistrictId\", \"NormalizedName\")"));

        var tables = new[] { "EducationalInstitutions", "EducationBoards", "EducationGroups", "ExaminationTypes", "Degrees", "Semesters", "AcademicSessions", "Designations", "Divisions" };
        foreach (var table in tables)
        {
            var indexes = await ColumnsAsync($"SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = '{table}'");
            Assert.Contains(indexes, i => i.StartsWith("CREATE UNIQUE INDEX") && i.Contains("(\"NormalizedName\")"));
        }

        // No foreign key from a configuration table may cascade: delete rule 'c' is CASCADE.
        var cascades = await ColumnsAsync(
            "SELECT conrelid::regclass::text FROM pg_constraint WHERE contype = 'f' AND confdeltype = 'c' " +
            "AND conrelid::regclass::text IN ('\"Batches\"','\"Districts\"','\"Upazilas\"')");
        Assert.Empty(cascades);
        var restrictive = await ColumnsAsync(
            "SELECT conname FROM pg_constraint WHERE contype = 'f' AND confdeltype IN ('r','a') " +
            "AND conrelid::regclass::text IN ('\"Batches\"','\"Districts\"','\"Upazilas\"')");
        Assert.Equal(5, restrictive.Count);
    }

    [PostgresFact]
    public async Task Stored_timestamps_and_text_limits_match_the_model()
    {
        var overlong = Named(new string('x', NameNormalizer.MaxNameLength), new Degree());
        await SaveAsync(overlong);

        await using var db = Db.CreateRuntimeContext();
        db.Add(Named(new string('y', NameNormalizer.MaxNameLength + 1), new Degree()));
        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
    }

    [PostgresFact]
    public async Task Degree_credit_total_is_stored_as_required_positive_numeric_with_two_decimal_places()
    {
        var degree = Named(Unique("Credit total"), new Degree());
        degree.SetCreditsToBeCompleted(120.50m);
        await SaveAsync(degree);

        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);
        await using (var metadata = new NpgsqlCommand(
            "SELECT data_type, numeric_precision, numeric_scale, is_nullable FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name = 'Degrees' AND column_name = 'CreditsToBeCompleted'",
            connection))
        await using (var reader = await metadata.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("numeric", reader.GetString(0));
            Assert.Equal(8, reader.GetInt32(1));
            Assert.Equal(2, reader.GetInt32(2));
            Assert.Equal("NO", reader.GetString(3));
        }

        await using (var persisted = new NpgsqlCommand("SELECT \"CreditsToBeCompleted\" FROM \"Degrees\" WHERE \"Id\" = @id", connection))
        {
            persisted.Parameters.AddWithValue("id", degree.Id);
            Assert.Equal(120.50m, (decimal)(await persisted.ExecuteScalarAsync())!);
        }

        await using var invalid = new NpgsqlCommand("UPDATE \"Degrees\" SET \"CreditsToBeCompleted\" = 0 WHERE \"Id\" = @id", connection);
        invalid.Parameters.AddWithValue("id", degree.Id);
        var error = await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }
}
