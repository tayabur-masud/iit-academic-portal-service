using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Configuration;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// The Grade scale on a real PostgreSQL 18 database: the migration's table, constraints, and unique live letter grade; the
/// seed-grades.sql script, which loads the ten initial bands into an empty table only; and full-scale replacements serialized by the
/// advisory lock. Each test uses its own database. Set AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class GradeScalePostgresTests
{
    private static readonly (string Letter, decimal Lower, decimal Upper, bool Inclusive, decimal Numeric, string Comment)[] Initial =
        GradeScaleTests.Initial;

    /// <summary>The fixed ids of the ten initial bands, highest first: version 5 UUIDs of "grade|LETTER" (see seed-grades.sql).</summary>
    private static readonly Guid[] SeedIds =
    [
        new("0ebd496e-c1c2-572e-9973-00adb99eee78"), new("85eac169-0145-55d8-8790-8d348e1a80e7"), new("1d39661a-064c-58e1-a8c2-afe3ce1bbf23"),
        new("d5e267a5-356d-5590-8f7f-030b01c5ec15"), new("f1327f34-5a08-56af-af5d-df17a64c024a"), new("688bb921-42b9-5f84-9958-ca41db03dfc9"),
        new("afcee7ed-eda7-5e03-844a-01aea8ffe60f"), new("f58dc5e2-77d5-52be-9126-e96171d660a4"), new("5ee0ac05-278c-502e-a735-505d915788b1"),
        new("309422f3-8bd8-53db-93b3-43be71326a9d"),
    ];

    private static async Task SeedAsync(PostgresTestDatabase database)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "postgresql", "seed-grades.sql"));
        await using var ddl = await database.OpenAsync(database.DdlConnectionString);
        await PostgresTestDatabase.ExecuteAsync(ddl, script);
    }

    private static async Task<List<string>> RowsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task<List<Grade>> LiveGradesAsync(PostgresTestDatabase database)
    {
        await using var db = database.CreateRuntimeContext();
        return await db.Grades.AsNoTracking().OrderByDescending(g => g.MarksLowerBound).ToListAsync();
    }

    [PostgresFact]
    public async Task The_migrations_leave_grades_empty_and_the_seed_script_loads_the_ten_bands_with_fixed_ids()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Assert.Empty(await LiveGradesAsync(database));

        await SeedAsync(database);

        var grades = await LiveGradesAsync(database);

        Assert.Equal(Initial.Select(b => b.Letter), grades.Select(g => g.Name));
        for (var i = 0; i < Initial.Length; i++)
        {
            Assert.Equal(Initial[i].Lower, grades[i].MarksLowerBound);
            Assert.Equal(Initial[i].Upper, grades[i].MarksUpperBound);
            Assert.Equal(Initial[i].Inclusive, grades[i].UpperBoundInclusive);
            Assert.Equal(Initial[i].Numeric, grades[i].NumericGrade);
            Assert.Equal(Initial[i].Comment, grades[i].Comment);
            Assert.True(grades[i].IsActive);
            Assert.Null(grades[i].CreatedByUserId);
        }

        Assert.Equal(SeedIds, grades.Select(g => g.Id));
    }

    [PostgresFact]
    public async Task The_grade_table_enforces_bounds_numeric_grade_comment_and_a_unique_live_letter()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await SeedAsync(database);
        await using var ddl = await database.OpenAsync(database.DdlConnectionString);
        var aPlus = SeedIds[0];

        foreach (var (set, state) in new[]
        {
            ("\"MarksLowerBound\" = -1", PostgresErrorCodes.CheckViolation),
            ("\"MarksUpperBound\" = 100.5", PostgresErrorCodes.CheckViolation),
            ("\"MarksLowerBound\" = 100", PostgresErrorCodes.CheckViolation),
            ("\"NumericGrade\" = 4.01", PostgresErrorCodes.NumericValueOutOfRange),
            ("\"NumericGrade\" = -0.01", PostgresErrorCodes.CheckViolation),
            ("\"Comment\" = '  '", PostgresErrorCodes.CheckViolation),
            ("\"Name\" = 'A', \"NormalizedName\" = 'A'", PostgresErrorCodes.UniqueViolation),
        })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresTestDatabase.ExecuteAsync(ddl, $"UPDATE \"Grades\" SET {set} WHERE \"Id\" = '{aPlus}'"));
            Assert.True(error.SqlState == state || error.SqlState == PostgresErrorCodes.CheckViolation, $"{set}: {error.SqlState}");
        }

        // Only a band ending at 100 may include its upper bound.
        var inclusive = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresTestDatabase.ExecuteAsync(ddl, $"UPDATE \"Grades\" SET \"UpperBoundInclusive\" = true WHERE \"Id\" = '{SeedIds[1]}'"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, inclusive.SqlState);

        // A deleted band leaves the unique index, so its letter can be used again.
        await PostgresTestDatabase.ExecuteAsync(ddl, $"UPDATE \"Grades\" SET \"IsDeleted\" = true, \"DeletedAtUtc\" = now() WHERE \"Id\" = '{aPlus}'");
        await PostgresTestDatabase.ExecuteAsync(ddl,
            "INSERT INTO \"Grades\" (\"Id\", \"Name\", \"NormalizedName\", \"IsActive\", \"MarksLowerBound\", \"MarksUpperBound\", \"UpperBoundInclusive\", \"NumericGrade\", \"Comment\") " +
            "VALUES (gen_random_uuid(), 'A+', 'A+', true, 80, 100, true, 4.00, 'Again')");

        var columns = await RowsAsync(ddl,
            "SELECT column_name || ':' || data_type || ':' || coalesce(numeric_precision::text, '-') || ':' || coalesce(numeric_scale::text, '-') " +
            "FROM information_schema.columns WHERE table_name = 'Grades' AND column_name IN ('MarksLowerBound', 'MarksUpperBound', 'NumericGrade') ORDER BY column_name");
        Assert.Equal(["MarksLowerBound:numeric:-:-", "MarksUpperBound:numeric:-:-", "NumericGrade:numeric:3:2"], columns);
        var keys = await RowsAsync(ddl, "SELECT conname FROM pg_constraint WHERE contype = 'f' AND conrelid = '\"Grades\"'::regclass");
        Assert.Empty(keys);
    }

    [PostgresFact]
    public async Task Running_the_seed_again_never_overwrites_admin_edits_or_restores_deleted_bands()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await SeedAsync(database);
        await using var ddl = await database.OpenAsync(database.DdlConnectionString);
        await PostgresTestDatabase.ExecuteAsync(ddl, $"UPDATE \"Grades\" SET \"Comment\" = 'Edited by an Admin', \"NumericGrade\" = 3.90 WHERE \"Id\" = '{SeedIds[0]}'");
        await PostgresTestDatabase.ExecuteAsync(ddl, $"UPDATE \"Grades\" SET \"IsDeleted\" = true, \"DeletedAtUtc\" = now() WHERE \"Id\" = '{SeedIds[8]}'");

        await SeedAsync(database);

        var grades = await LiveGradesAsync(database);
        Assert.Equal(9, grades.Count);
        Assert.Equal("Edited by an Admin", grades[0].Comment);
        Assert.Equal(3.90m, grades[0].NumericGrade);
        Assert.DoesNotContain(grades, g => g.Name == "D");
    }

    [PostgresFact]
    public async Task The_seed_adds_nothing_while_any_row_remains_and_reseeds_a_table_emptied_with_delete()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await SeedAsync(database);
        await using var ddl = await database.OpenAsync(database.DdlConnectionString);

        // Every band deleted in the portal: the rows are only marked, so the table is not empty and nothing comes back.
        await PostgresTestDatabase.ExecuteAsync(ddl, "UPDATE \"Grades\" SET \"IsDeleted\" = true, \"DeletedAtUtc\" = now()");
        await SeedAsync(database);
        Assert.Empty(await LiveGradesAsync(database));

        // Removed outright, the table is empty again and the script loads the same ten bands with the same ids.
        await PostgresTestDatabase.ExecuteAsync(ddl, "DELETE FROM \"Grades\"");
        await SeedAsync(database);
        Assert.Equal(SeedIds, (await LiveGradesAsync(database)).Select(g => g.Id));
    }

    [PostgresFact]
    public async Task Concurrent_valid_replacements_serialize_so_the_committed_scale_is_always_a_complete_partition()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var database = await PostgresTestDatabase.CreateAsync();
            await SeedAsync(database);
            var start = await LiveGradesAsync(database);

            // Each removes D on its own way; applied together without the lock they would overlap on 40-45.
            var extendC = Bands(start, "D", ("C", lower: 40m, upper: null));
            var extendF = Bands(start, "D", ("F", lower: null, upper: 45m));

            using var gate = new LockGate();
            var results = await Task.WhenAll(
                Task.Run(() => ReplaceAsync(database, extendC, gate)),
                Task.Run(() => ReplaceAsync(database, extendF, gate)));

            Assert.Equal(2, gate.LockCalls);
            Assert.All(results, r => Assert.True(r.Outcome == GradeScaleOutcome.Ok, string.Join("; ", r.Errors?.SelectMany(e => e.Value) ?? [])));
            var final = await LiveGradesAsync(database);
            AssertCompletePartition(final.Where(g => g.IsActive).ToList());
            Assert.DoesNotContain(final, g => g.Name == "D");
        }
    }

    /// <summary>The live bands as a replacement request, without one letter, and with one band's bounds moved.</summary>
    private static List<GradeBandInput> Bands(List<Grade> grades, string omit, (string Letter, decimal? lower, decimal? upper) move) =>
        grades.Where(g => g.Name != omit).Select(g => new GradeBandInput(
            g.Id, g.Name,
            g.Name == move.Letter && move.lower is { } lower ? lower : g.MarksLowerBound,
            g.Name == move.Letter && move.upper is { } upper ? upper : g.MarksUpperBound,
            g.UpperBoundInclusive, g.NumericGrade, g.Comment, g.IsActive)).ToList();

    private static void AssertCompletePartition(List<Grade> active)
    {
        var ordered = active.OrderBy(g => g.MarksLowerBound).ToList();
        Assert.Equal(0m, ordered[0].MarksLowerBound);
        for (var i = 1; i < ordered.Count; i++)
        {
            Assert.Equal(ordered[i - 1].MarksUpperBound, ordered[i].MarksLowerBound);
        }

        Assert.Equal(100m, ordered[^1].MarksUpperBound);
        Assert.True(ordered[^1].UpperBoundInclusive);
    }

    private static async Task<GradeScaleResult> ReplaceAsync(PostgresTestDatabase database, List<GradeBandInput> bands, LockGate gate)
    {
        await using var db = database.CreateRuntimeContext();
        var store = new GatedGradeStore(new GradeScaleStore(db), gate);
        var service = new GradeScaleService(store, new NoopRecorder(), TimeProvider.System, new NoRequest(), new NoActors());
        return await service.ReplaceAsync(bands, CancellationToken.None);
    }

    private sealed class LockGate : IDisposable
    {
        private readonly Barrier barrier = new(2);
        private int lockCalls;

        public int LockCalls => Volatile.Read(ref lockCalls);

        public async Task LockAsync(IGradeScaleStore store, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref lockCalls);
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Both replacements must reach the Grade scale lock.");
            }

            await store.LockScaleAsync(cancellationToken);
        }

        public void Dispose() => barrier.Dispose();
    }

    /// <summary>The production store, with both replacements held at the lock until both have started and read nothing yet.</summary>
    private sealed class GatedGradeStore(IGradeScaleStore inner, LockGate gate) : IGradeScaleStore
    {
        public Task<ITeacherTransaction> BeginTransactionAsync(CancellationToken cancellationToken) => inner.BeginTransactionAsync(cancellationToken);

        public Task LockScaleAsync(CancellationToken cancellationToken) => gate.LockAsync(inner, cancellationToken);

        public async Task<List<Grade>> LoadLiveForChangeAsync(CancellationToken cancellationToken)
        {
            var grades = await inner.LoadLiveForChangeAsync(cancellationToken);
            // Widen the window in which an unserialized second replacement would read the same, stale scale.
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            return grades;
        }

        public Task<IReadOnlyList<Grade>> ListAsync(string? normalizedSearch, ConfigurationStatus status, CancellationToken cancellationToken) =>
            inner.ListAsync(normalizedSearch, status, cancellationToken);

        public Task<Grade?> FindAsync(Guid id, CancellationToken cancellationToken) => inner.FindAsync(id, cancellationToken);

        public void Add(Grade grade) => inner.Add(grade);

        public Task SaveAsync(CancellationToken cancellationToken) => inner.SaveAsync(cancellationToken);
    }

    private sealed class NoopRecorder : IAuditEventRecorder
    {
        public void Stage(AuditEventRequest request) { }

        public Task RecordSecurityAsync(AuditEventRequest request) => Task.CompletedTask;
    }

    private sealed class NoRequest : IAuditRequestContext
    {
        public string? ActorUserId => null;

        public string? CorrelationId => null;

        public string? Source => null;
    }

    private sealed class NoActors : IAuditActorDirectory
    {
        public Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }
}
