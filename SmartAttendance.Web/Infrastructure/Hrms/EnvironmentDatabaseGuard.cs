using System.Data.Common;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// حارس فصل البيئات — يمنع أن يلمس تشغيلٌ غير إنتاجي قاعدة SmartAttendance
/// إلا في حالة تطوير محلية صريحة ومقيّدة بخادم SQL محلي.
/// </summary>
public static class EnvironmentDatabaseGuard
{
    public const string ProductionDatabaseName = "SmartAttendance";

    public static string? DatabaseName(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        try
        {
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            foreach (var key in new[] { "Database", "Initial Catalog" })
            {
                if (builder.TryGetValue(key, out var value) &&
                    value?.ToString() is { Length: > 0 } name)
                {
                    return name;
                }
            }
        }
        catch (ArgumentException)
        {
            // نص اتصال تالف — لا نخمّن.
        }

        return null;
    }

    public static string? ServerName(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;

        try
        {
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            foreach (var key in new[]
                     {
                         "Server",
                         "Data Source",
                         "Address",
                         "Addr",
                         "Network Address"
                     })
            {
                if (builder.TryGetValue(key, out var value) &&
                    value?.ToString() is { Length: > 0 } name)
                {
                    return name.Trim();
                }
            }
        }
        catch (ArgumentException)
        {
            // نص اتصال تالف — لا نخمّن.
        }

        return null;
    }

    public static bool IsLocalSqlServer(string? connectionString)
    {
        var server = ServerName(connectionString);
        if (string.IsNullOrWhiteSpace(server)) return false;

        var normalized = server.Trim();

        return normalized.Equals(".", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("(local)", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("::1", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(@".\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(@"(local)\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(@"localhost\", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(@"127.0.0.1\", StringComparison.OrdinalIgnoreCase);
    }

    public static string? Validate(
        string? environmentName,
        string? connectionString,
        bool allowLocalDevelopmentOnPrimaryDatabase = false)
    {
        var isProduction = string.Equals(
            environmentName,
            "Production",
            StringComparison.OrdinalIgnoreCase);

        if (isProduction) return null;

        var database = DatabaseName(connectionString);
        if (database is null) return null;

        if (!string.Equals(
                database,
                ProductionDatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var isDevelopment = string.Equals(
            environmentName,
            "Development",
            StringComparison.OrdinalIgnoreCase);

        if (allowLocalDevelopmentOnPrimaryDatabase &&
            isDevelopment &&
            IsLocalSqlServer(connectionString))
        {
            return null;
        }

        return $"""
            رُفض الإقلاع: البيئة «{environmentName ?? "غير محدَّدة"}» تشير لقاعدة «{database}».
            تشغيل غير إنتاجي على قاعدة SmartAttendance مرفوض افتراضياً.
            للتطوير المحلي فقط يمكن السماح صراحةً عندما يكون SQL Server محلياً عبر
            Development:AllowSmartAttendanceDatabase=true.
            """;
    }
}