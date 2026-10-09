using System.Security.Cryptography;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>
/// PostgreSQL-backed tests run only when AUDIT_TEST_POSTGRES holds an administrator connection string. Plain
/// <c>dotnet test</c> without it still passes: these tests are skipped with a clear message.
/// </summary>
public static class PostgresTestEnvironment
{
    public const string VariableName = "AUDIT_TEST_POSTGRES";

    public static string? AdminConnectionString => Environment.GetEnvironmentVariable(VariableName);

    public static bool Available => !string.IsNullOrWhiteSpace(AdminConnectionString);
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!PostgresTestEnvironment.Available)
        {
            Skip = $"Set {PostgresTestEnvironment.VariableName} to a PostgreSQL administrator connection string to run this test.";
        }
    }
}

/// <summary>
/// A disposable database created like a real deployment: a schema-owner (DDL) role applies the migrations and the
/// runtime grants script, and a separate restricted runtime role is what the API would connect as.
/// </summary>
public sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;

    private PostgresTestDatabase(string adminConnectionString, string database, string ddlRole, string runtimeRole, string ddlConnection, string runtimeConnection)
    {
        this.adminConnectionString = adminConnectionString;
        DatabaseName = database;
        DdlRole = ddlRole;
        RuntimeRole = runtimeRole;
        DdlConnectionString = ddlConnection;
        RuntimeConnectionString = runtimeConnection;
    }

    public string DatabaseName { get; }

    public string DdlRole { get; }

    public string RuntimeRole { get; }

    public string DdlConnectionString { get; }

    public string RuntimeConnectionString { get; }

    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var admin = PostgresTestEnvironment.AdminConnectionString
            ?? throw new InvalidOperationException($"{PostgresTestEnvironment.VariableName} is not set.");

        // On Windows, resolving "localhost" can stall under many concurrent connections (the IPv6 address is tried
        // first), which makes the concurrency tests time out. The tests use the IPv4 loopback address instead.
        var adminBuilder = new NpgsqlConnectionStringBuilder(admin);
        if (string.Equals(adminBuilder.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            adminBuilder.Host = "127.0.0.1";
            admin = adminBuilder.ConnectionString;
        }

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var database = $"audit_test_{suffix}";
        var ddlRole = $"audit_ddl_{suffix}";
        var runtimeRole = $"audit_rt_{suffix}";
        var ddlPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var runtimePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, $"CREATE ROLE \"{ddlRole}\" LOGIN PASSWORD '{ddlPassword}'");
            await ExecuteAsync(connection, $"CREATE ROLE \"{runtimeRole}\" LOGIN PASSWORD '{runtimePassword}'");
            await ExecuteAsync(connection, $"CREATE DATABASE \"{database}\" OWNER \"{ddlRole}\"");
        }

        string Connection(string user, string password) => new NpgsqlConnectionStringBuilder(admin)
        {
            Database = database,
            Username = user,
            Password = password,
            Pooling = true,
            MaxPoolSize = 40,
        }.ConnectionString;

        var instance = new PostgresTestDatabase(admin, database, ddlRole, runtimeRole, Connection(ddlRole, ddlPassword), Connection(runtimeRole, runtimePassword));

        // Exactly the production order: the schema owner applies the real migrations, then the grants script.
        await using (var ddlContext = instance.CreateContext(instance.DdlConnectionString))
        {
            await ddlContext.Database.MigrateAsync();
        }

        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "postgresql", "runtime-grants.sql"));
        await using (var connection = new NpgsqlConnection(instance.DdlConnectionString))
        {
            await connection.OpenAsync();
            await ExecuteAsync(connection, script.Replace(":\"runtime_role\"", $"\"{runtimeRole}\""));
        }

        return instance;
    }

    public PortalDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<PortalDbContext>().UseNpgsql(connectionString).Options);

    public PortalDbContext CreateRuntimeContext() => CreateContext(RuntimeConnectionString);

    public IDbContextFactory<PortalDbContext> CreateRuntimeFactory() => new RuntimeFactory(this);

    public async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)");
        await ExecuteAsync(connection, $"DROP ROLE IF EXISTS \"{RuntimeRole}\"");
        await ExecuteAsync(connection, $"DROP ROLE IF EXISTS \"{DdlRole}\"");
    }

    private sealed class RuntimeFactory(PostgresTestDatabase database) : IDbContextFactory<PortalDbContext>
    {
        public PortalDbContext CreateDbContext() => database.CreateRuntimeContext();
    }
}

/// <summary>Creates the disposable database once per test class, and only when PostgreSQL is available.</summary>
public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    public PostgresTestDatabase? Database { get; private set; }

    public async Task InitializeAsync()
    {
        if (PostgresTestEnvironment.Available)
        {
            Database = await PostgresTestDatabase.CreateAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (Database is not null)
        {
            await Database.DisposeAsync();
        }
    }
}
