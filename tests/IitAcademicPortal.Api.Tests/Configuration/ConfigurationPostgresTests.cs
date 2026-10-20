using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Configuration;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
            degree.SetMinimumCgpaForCompletion(2.50m);
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
        degree.SetMinimumCgpaForCompletion(2.50m);
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

    [PostgresFact]
    public async Task Degree_minimum_cgpa_is_a_required_numeric_3_2_between_0_and_4_with_no_default()
    {
        var degree = Named(Unique("CGPA"), new Degree());
        degree.SetMinimumCgpaForCompletion(3.25m);
        await SaveAsync(degree);

        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);
        await using (var metadata = new NpgsqlCommand(
            "SELECT data_type, numeric_precision, numeric_scale, is_nullable, column_default IS NULL FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name = 'Degrees' AND column_name = 'MinimumCgpaForCompletion'",
            connection))
        await using (var reader = await metadata.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("numeric", reader.GetString(0));
            Assert.Equal(3, reader.GetInt32(1));
            Assert.Equal(2, reader.GetInt32(2));
            Assert.Equal("NO", reader.GetString(3));
            Assert.True(reader.GetBoolean(4), "The 2.50 backfill default must not stay on the column.");
        }

        foreach (var bad in new[] { "0", "4.01", "-1" })
        {
            await using var invalid = new NpgsqlCommand($"UPDATE \"Degrees\" SET \"MinimumCgpaForCompletion\" = {bad} WHERE \"Id\" = @id", connection);
            invalid.Parameters.AddWithValue("id", degree.Id);
            var error = await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
    }

    [PostgresFact]
    public async Task Degrees_that_exist_before_the_migration_get_a_minimum_cgpa_of_2_50()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var ddl = database.CreateContext(database.DdlConnectionString))
        {
            var migrator = ddl.GetService<IMigrator>();
            await migrator.MigrateAsync("20261019124612_Courses");
            await using var connection = await database.OpenAsync(database.DdlConnectionString);
            await PostgresTestDatabase.ExecuteAsync(connection,
                "INSERT INTO \"Degrees\" (\"Id\", \"Name\", \"NormalizedName\", \"IsActive\", \"CreditsToBeCompleted\") " +
                "VALUES ('6f1c1f6e-0000-4000-8000-000000000001', 'Old degree', 'OLD DEGREE', true, 36)");
            await migrator.MigrateAsync();
        }

        await using var verify = await database.OpenAsync(database.DdlConnectionString);
        await using var command = new NpgsqlCommand(
            "SELECT \"MinimumCgpaForCompletion\" FROM \"Degrees\" WHERE \"Id\" = '6f1c1f6e-0000-4000-8000-000000000001'", verify);
        Assert.Equal(2.50m, (decimal)(await command.ExecuteScalarAsync())!);
    }

    private static Course NewCourse(Guid degreeId, string? code = null, decimal credit = 3m, decimal? theory = null, decimal? lab = null, Guid? prerequisite = null)
    {
        var course = Named(Unique("Course"), new Course { DegreeId = degreeId, CreditHours = credit, TheoryCreditHours = theory, LabCreditHours = lab, PrerequisiteCourseId = prerequisite });
        course.SetCode(code ?? Guid.NewGuid().ToString("N")[..10]);
        return course;
    }

    private async Task<string?> SqlStateAsync(params ConfigurationRecord[] records)
    {
        await using var db = Db.CreateRuntimeContext();
        db.AddRange(records);
        var error = await Assert.ThrowsAnyAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => db.SaveChangesAsync());
        return (error.InnerException as PostgresException)?.SqlState;
    }

    [PostgresFact]
    public async Task Course_credit_checks_reject_a_non_positive_total_parts_outside_it_and_parts_that_do_not_sum()
    {
        var degree = Named(Unique("Course degree"), new Degree());
        await SaveAsync(degree);

        await SaveAsync(NewCourse(degree.Id, theory: 3m), NewCourse(degree.Id, lab: 0m), NewCourse(degree.Id, credit: 4.5m, theory: 3m, lab: 1.5m));
        Assert.Equal("23514", await SqlStateAsync(NewCourse(degree.Id, credit: 0m)));
        Assert.Equal("23514", await SqlStateAsync(NewCourse(degree.Id, theory: 3.5m)));
        Assert.Equal("23514", await SqlStateAsync(NewCourse(degree.Id, lab: -1m)));
        Assert.Equal("23514", await SqlStateAsync(NewCourse(degree.Id, theory: 1m, lab: 1m)));

        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);
        await using var command = new NpgsqlCommand(
            "SELECT numeric_precision, numeric_scale FROM information_schema.columns WHERE table_name = 'Courses' AND column_name = 'TheoryCreditHours'", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(8, reader.GetInt32(0));
        Assert.Equal(2, reader.GetInt32(1));
    }

    [PostgresFact]
    public async Task A_course_code_is_unique_among_live_courses_of_every_degree_but_course_names_may_repeat()
    {
        var first = Named(Unique("Code degree A"), new Degree());
        var second = Named(Unique("Code degree B"), new Degree());
        await SaveAsync(first, second);
        var code = "UQ-" + Guid.NewGuid().ToString("N")[..8];
        var existing = NewCourse(first.Id, code);
        existing.IsActive = false;
        var sameName = NewCourse(first.Id);
        sameName.Rename(existing.Name);
        await SaveAsync(existing, sameName);

        Assert.Equal(ConfigurationConflictKind.Duplicate, await ConflictAsync(NewCourse(second.Id, code.ToLowerInvariant())));

        await using (var db = Db.CreateRuntimeContext())
        {
            var stored = await db.Courses.FindAsync(existing.Id);
            stored!.MarkDeleted(DateTime.UtcNow, null);
            await db.SaveChangesAsync();
        }

        await SaveAsync(NewCourse(second.Id, code));
    }

    [PostgresFact]
    public async Task Course_degree_and_prerequisite_references_are_restrictive_and_indexed()
    {
        var degree = Named(Unique("Ref degree"), new Degree());
        await SaveAsync(degree);
        var basics = NewCourse(degree.Id);
        await SaveAsync(basics);
        var advanced = NewCourse(degree.Id, prerequisite: basics.Id);
        await SaveAsync(advanced);

        await using var ddl = await Db.OpenAsync(Db.DdlConnectionString);
        foreach (var (table, id) in new[] { ("Degrees", degree.Id), ("Courses", basics.Id) })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => PostgresTestDatabase.ExecuteAsync(ddl, $"DELETE FROM \"{table}\" WHERE \"Id\" = '{id}'"));
            Assert.Contains(error.SqlState, new[] { "23001", "23503" });
        }

        var self = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresTestDatabase.ExecuteAsync(ddl, $"UPDATE \"Courses\" SET \"PrerequisiteCourseId\" = \"Id\" WHERE \"Id\" = '{basics.Id}'"));
        Assert.Equal("23514", self.SqlState);

        await using var command = new NpgsqlCommand(
            "SELECT indexname FROM pg_indexes WHERE tablename = 'Courses' ORDER BY indexname", ddl);
        var indexes = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                indexes.Add(reader.GetString(0));
            }
        }

        Assert.Contains("IX_Courses_DegreeId", indexes);
        Assert.Contains("IX_Courses_PrerequisiteCourseId", indexes);
        Assert.Contains("IX_Courses_NormalizedCode", indexes);
    }

    [PostgresFact]
    public async Task City_corporation_names_are_unique_among_live_rows_of_a_district_and_its_district_key_is_restrictive()
    {
        var division = Named(Unique("CC division"), new Division());
        await SaveAsync(division);
        var (district, elsewhere) = (Named(Unique("CC district"), new District { DivisionId = division.Id }), Named(Unique("CC other"), new District { DivisionId = division.Id }));
        await SaveAsync(district, elsewhere);
        var name = Unique("Metro");
        var city = Named(name, new CityCorporation { DistrictId = district.Id });
        await SaveAsync(city);

        Assert.Equal(ConfigurationConflictKind.Duplicate, await ConflictAsync(Named(name.ToUpperInvariant(), new CityCorporation { DistrictId = district.Id })));
        await SaveAsync(Named(name, new CityCorporation { DistrictId = elsewhere.Id }));
        // An Upazila of the same name in the same District is a different entity type and does not collide.
        await SaveAsync(Named(name, new Upazila { DistrictId = district.Id }));

        await using var ddl = await Db.OpenAsync(Db.DdlConnectionString);
        var error = await Assert.ThrowsAsync<PostgresException>(() => PostgresTestDatabase.ExecuteAsync(ddl, $"DELETE FROM \"Districts\" WHERE \"Id\" = '{district.Id}'"));
        Assert.Contains(error.SqlState, new[] { "23001", "23503" });

        // A soft-deleted row leaves the unique index, so the name is free again in that District.
        await PostgresTestDatabase.ExecuteAsync(ddl, $"UPDATE \"CityCorporations\" SET \"IsDeleted\" = true, \"DeletedAtUtc\" = now() WHERE \"Id\" = '{city.Id}'");
        await SaveAsync(Named(name, new CityCorporation { DistrictId = district.Id }));

        async Task<List<string>> RowsAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, ddl);
            var list = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(reader.GetString(0));
            }

            return list;
        }

        var indexes = await RowsAsync("SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'CityCorporations'");
        Assert.Contains(indexes, i => i.StartsWith("CREATE UNIQUE INDEX") && i.Contains("(\"DistrictId\", \"NormalizedName\")") && i.Contains("\"IsDeleted\" = false"));
        Assert.DoesNotContain(indexes, i => i.Contains("Code"));
        var keys = await RowsAsync(
            "SELECT confdeltype::text || ':' || confrelid::regclass::text FROM pg_constraint WHERE contype = 'f' AND conrelid = '\"CityCorporations\"'::regclass");
        // One key, to Districts, restrict or no action (never cascade).
        Assert.Matches("^[ra]:\"Districts\"$", Assert.Single(keys));
    }
}
