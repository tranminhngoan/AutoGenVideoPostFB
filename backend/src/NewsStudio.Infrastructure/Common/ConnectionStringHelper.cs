using System;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace NewsStudio.Infrastructure.Common;

/// <summary>
/// Utility to resolve PostgreSQL connection strings from standard .NET configuration 
/// or cloud platform environment variables such as Railway's DATABASE_URL.
/// </summary>
public static class ConnectionStringHelper
{
    public static string ResolveConnectionString(IConfiguration configuration)
    {
        // 1. Check Railway/Heroku/Render standard DATABASE_URL environment variable
        var databaseUrl = configuration["DATABASE_URL"] 
                          ?? Environment.GetEnvironmentVariable("DATABASE_URL");

        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            var parsed = ParseDatabaseUrl(databaseUrl);
            if (!string.IsNullOrWhiteSpace(parsed))
            {
                return parsed;
            }
        }

        // 2. Check standard .NET ConnectionStrings:DefaultConnection
        var defaultConn = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(defaultConn))
        {
            // If DefaultConnection itself is formatted as postgres:// or postgresql://
            if (defaultConn.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                defaultConn.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            {
                var parsed = ParseDatabaseUrl(defaultConn);
                if (!string.IsNullOrWhiteSpace(parsed))
                {
                    return parsed;
                }
            }

            return defaultConn;
        }

        // 3. Fallback for local development
        return "Host=localhost;Port=5432;Database=newsstudio_db;Username=postgres;Password=postgres";
    }

    /// <summary>
    /// Parses a URI format (e.g. postgresql://user:password@host:port/dbname) into an Npgsql key-value connection string.
    /// </summary>
    public static string ParseDatabaseUrl(string databaseUrl)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(databaseUrl))
                return string.Empty;

            var uri = new Uri(databaseUrl);
            var userInfo = uri.UserInfo.Split(':');
            var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "postgres";
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 5432;
            var database = uri.AbsolutePath.TrimStart('/');

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Username = username,
                Password = password,
                Database = database,
                SslMode = SslMode.Prefer,
                Pooling = true,
                KeepAlive = 30
            };

            return builder.ConnectionString;
        }
        catch
        {
            // If URI parsing fails, return raw string to let Npgsql attempt native parsing
            return databaseUrl;
        }
    }
}
