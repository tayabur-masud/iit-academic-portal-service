using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Configuration;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Infrastructure.Persistence;
using IitAcademicPortal.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// The Grade scale on a real PostgreSQL 18 database: the migration's table, constraints, unique live letter grade, and the ten
/// seeded bands; an insert-only seed that never overwrites Admin edits; and full-scale replacements serialized by the advisory
/// lock. Each test uses its own database, so the seed is exactly what the migration wrote. Set AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class GradeScalePostgresTests
{
    private static readonly (string Letter, decimal Lower, decimal Upper, bool Inclusive, decimal Numeric, string Comment)[] Initial =
        GradeScaleTests.Initial;

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
    public async Task The_migration_seeds_the_ten_initial_bands_with_stable_ids()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();

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

        Assert.Equal(GradeSeed.InitialIds.Order(), grades.Select(g => g.Id).Order());
    }

    [PostgresFact]
    public async Task The_grade_table_enforces_bounds_numeric_grade_comment_and_a_unique_live_letter()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var ddl = await database.OpenAsync(database.DdlConnectionString);
        var aPlus = GradeSeed.InitialIds[0];

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
            PostgresTestDatabase.ExecuteAsync(ddl, $"UPDATE \"Grades\" SET \"UpperBoundInclusive\" = true WHERE \"Id\" = '{GradeSeed.InitialIds[1]}'"));
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
        await using var ddl = await database.OpenAsync(database.DdlConnectionString);
        await PostgresTestDatabase.ExecuteAsync(ddl, $"UPDATE \"Grades\" SET \"Comment\" = 'Edited by an Admin', \"NumericGrade\" = 3.90 WHERE \"Id\" = '{GradeSeed.InitialIds[0]}'");
        await PostgresTestDatabase.ExecuteAsync(ddl, $"UPDATE \"Grades\" SET \"IsDeleted\" = true, \"DeletedAtUtc\" = now() WHERE \"Id\" = '{GradeSeed.InitialIds[8]}'");

        await PostgresTestDatabase.ExecuteAsync(ddl, CityCorporationsDegreeMinimumCgpaAndGrades.InitialGradesSql);

        var grades = await LiveGradesAsync(database);
        Assert.Equal(9, grades.Count);
        Assert.Equal("Edited by an Admin", grades[0].Comment);
        Assert.Equal(3.90m, grades[0].NumericGrade);
        Assert.DoesNotContain(grades, g => g.Name == "D");
    }

    [PostgresFact]
    public async Task Concurrent_valid_replacements_serialize_so_the_committed_scale_is_always_a_complete_partition()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var database = await PostgresTestDatabase.CreateAsync();
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
