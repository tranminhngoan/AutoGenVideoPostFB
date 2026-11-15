using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace NewsStudio.Tests;

/// <summary>
/// Runs once when the test assembly loads, before any WebApplicationFactory boots the API.
///
/// 1. Points the API at a dedicated test database so integration tests never write their
///    fixtures into the real development database (they used to — that is where the hundreds
///    of "Test Metro Line Project" / "Dự án bị sập giữa chừng" rows came from).
/// 2. Enables simulation mode so AI / TTS / FFmpeg / Meta Graph calls can fall back to local
///    placeholder output and the suite runs offline.
///
/// Override the target with the TEST_DATABASE_URL environment variable
/// (postgresql://user:pass@host:port/dbname or an Npgsql key=value string).
/// </summary>
internal static class TestEnvironment
{
    public const string DefaultTestDatabaseName = "newsstudio_test";

    [ModuleInitializer]
    internal static void Initialize()
    {
        var connectionString = ResolveTestConnectionString();
        EnsureDatabaseExists(connectionString);

        // ConnectionStringHelper gives DATABASE_URL priority over ConnectionStrings:DefaultConnection,
        // so make sure a developer's real DATABASE_URL can never leak into the test run.
        Environment.SetEnvironmentVariable("DATABASE_URL", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connectionString);
        Environment.SetEnvironmentVariable("Simulation__Enabled", "true");

        // Keep test media out of the developer's shared temp folder (backend/tmp/newsstudio).
        Environment.SetEnvironmentVariable("Storage__TempStoragePath", Path.Combine(AppContext.BaseDirectory, "tmp", "newsstudio"));

        // The API shares user-secrets with the Worker, so a developer's real AI keys would otherwise be
        // loaded here and every test run would make billed Gemini/Claude calls. A whitespace value
        // overrides user-secrets and machine env vars, and the services treat it as "no key".
        Environment.SetEnvironmentVariable("Ai__Gemini__ApiKey", " ");
        Environment.SetEnvironmentVariable("Ai__Claude__ApiKey", " ");
        // Offline tests use the Edge-TTS path; the default Gemini voice needs a real key.
        Environment.SetEnvironmentVariable("Tts__DefaultVoice", "vi-VN-HoaiMyNeural");
    }

    /// <summary>
    /// Hangfire opens its storage connection before EF migrations run, so the database must
    /// already exist when the API boots. EF migrations then create the schema inside it.
    /// </summary>
    private static void EnsureDatabaseExists(string connectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var databaseName = target.Database;
        if (string.IsNullOrWhiteSpace(databaseName)) return;

        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false };
        using var conn = new NpgsqlConnection(admin.ConnectionString);
        conn.Open();

        using (var exists = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", conn))
        {
            exists.Parameters.AddWithValue("name", databaseName);
            if (exists.ExecuteScalar() != null) return;
        }

        // Identifiers cannot be parameterised; quote it safely.
        var quoted = "\"" + databaseName.Replace("\"", "\"\"") + "\"";
        using var create = new NpgsqlCommand($"CREATE DATABASE {quoted}", conn);
        create.ExecuteNonQuery();
    }

    private static string ResolveTestConnectionString()
    {
        var explicitUrl = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            return explicitUrl.StartsWith("postgres", StringComparison.OrdinalIgnoreCase)
                ? NewsStudio.Infrastructure.Common.ConnectionStringHelper.ParseDatabaseUrl(explicitUrl)
                : explicitUrl;
        }

        // Reuse host/credentials from the API's appsettings.json but swap the database name.
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var devConnection = config.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=newsstudio_db;Username=postgres;Password=postgres";

        var builder = new NpgsqlConnectionStringBuilder(devConnection)
        {
            Database = DefaultTestDatabaseName
        };
        return builder.ConnectionString;
    }
}
