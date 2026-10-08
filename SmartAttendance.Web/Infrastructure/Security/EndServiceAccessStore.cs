using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>Explicitly migrated, forward-only access deadlines; never enables an account.</summary>
public static class EndServiceAccessStore
{
    private static readonly object RequestCacheKey = new();

    public static async Task<Schedule?> GetForRequestAsync(ApplicationDbContext db, HttpContext context, int tenantId, string? username)
    {
        var key = (tenantId, (username ?? "").Trim().ToUpperInvariant());
        if (context.Items[RequestCacheKey] is not Dictionary<(int, string), Schedule?> entries)
            context.Items[RequestCacheKey] = entries = [];
        if (entries.TryGetValue(key, out var cached)) return cached;
        var result = await GetAsync(db, tenantId, username);
        entries[key] = result; // Request lifetime only: no grace/negative result carried to a later request.
        return result;
    }
    public const string FarewellPath = "/Account/Farewell";
    public const string MigrationSql = """
IF OBJECT_ID('EndServiceAccessSchedules', 'U') IS NULL
BEGIN
    CREATE TABLE EndServiceAccessSchedules (
        EndServiceId int NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        CompanyId int NOT NULL,
        NotificationEligibleAtUtc datetime2(7) NOT NULL,
        AccessEndsAtUtc datetime2(7) NOT NULL,
        Immediate bit NOT NULL,
        ClosedAtUtc datetime2(7) NULL,
        CreatedAtUtc datetime2(7) NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_EndServiceAccessSchedules_Employee
        ON EndServiceAccessSchedules(CompanyId, EmployeeId, EndServiceId);
    CREATE INDEX IX_EndServiceAccessSchedules_Due
        ON EndServiceAccessSchedules(ClosedAtUtc, AccessEndsAtUtc);
END;
""";

    public sealed record Schedule(int EndServiceId, int EmployeeId, int CompanyId,
        DateTimeOffset NotificationEligibleAtUtc, DateTimeOffset AccessEndsAtUtc);

    // No claim/query employee identifier is accepted. Tenant + authenticated username
    // resolve the actual account owner; active rehires and older service periods are excluded.
    public const string LookupSql = """
SELECT TOP (1) s.EndServiceId, s.EmployeeId, s.CompanyId,
    s.NotificationEligibleAtUtc, s.AccessEndsAtUtc
FROM AppLoginUsers u
JOIN Employees e ON e.Id = u.EmployeeId AND e.IsDeleted = 0 AND e.IsActive = 0
JOIN Companies c ON c.Id = e.CompanyId AND c.TenantId = u.TenantId
JOIN EndServiceAccessSchedules s ON s.EmployeeId = e.Id AND s.CompanyId = e.CompanyId
WHERE u.TenantId = @TenantId AND u.Username = @Username
  AND s.EndServiceId = (SELECT MAX(es.Id) FROM EmployeeEndServices es WHERE es.EmployeeId = e.Id);
""";

    public static async Task<Schedule?> GetAsync(ApplicationDbContext db, int tenantId, string? username)
    {
        if (tenantId <= 0 || string.IsNullOrWhiteSpace(username)) return null;
        // No runtime DDL or negative-result cache: an explicit migration activates the feature.
        if (!await IsReadyAsync(db)) return null;
        var rows = await HrmsDatabase.QueryAsync(db, LookupSql, command =>
        {
            HrmsDatabase.AddParameter(command, "@TenantId", tenantId);
            HrmsDatabase.AddParameter(command, "@Username", username.Trim());
        }, reader => new Schedule(HrmsDatabase.GetInt(reader, "EndServiceId"),
            HrmsDatabase.GetInt(reader, "EmployeeId"), HrmsDatabase.GetInt(reader, "CompanyId"),
            new DateTimeOffset(DateTime.SpecifyKind(Convert.ToDateTime(reader["NotificationEligibleAtUtc"]), DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(Convert.ToDateTime(reader["AccessEndsAtUtc"]), DateTimeKind.Utc))));
        return rows.FirstOrDefault();
    }

    public static async Task<bool> IsReadyAsync(ApplicationDbContext db) =>
        await HrmsDatabase.ScalarAsync<int>(db,
            "SELECT CASE WHEN OBJECT_ID('EndServiceAccessSchedules', 'U') IS NULL OR OBJECT_ID('EmployeeEndServices', 'U') IS NULL THEN 0 ELSE 1 END;", _ => { }) == 1;

    // The request guard enforces the deadline even if this worker is delayed or offline.
    // Lock the employment row, shared with termination/rehire writers, before disabling.
    public const string CloseDueSql = """
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @Due TABLE(EmployeeId int, CompanyId int, TenantId int, EndServiceId int);
INSERT @Due
SELECT e.Id, e.CompanyId, c.TenantId, s.EndServiceId
FROM Employees e WITH (UPDLOCK, HOLDLOCK)
JOIN Companies c ON c.Id = e.CompanyId
JOIN EndServiceAccessSchedules s ON s.EmployeeId = e.Id AND s.CompanyId = e.CompanyId
WHERE e.IsActive = 0 AND e.IsDeleted = 0 AND s.ClosedAtUtc IS NULL AND s.AccessEndsAtUtc <= @Now
  AND s.EndServiceId = (SELECT MAX(es.Id) FROM EmployeeEndServices es WHERE es.EmployeeId = e.Id);

UPDATE u SET IsActive = 0, SecurityStamp = CONVERT(nvarchar(64), NEWID()), UpdatedAt = @Now
FROM AppLoginUsers u JOIN @Due d ON d.EmployeeId = u.EmployeeId AND d.TenantId = u.TenantId
WHERE u.IsActive = 1;
IF OBJECT_ID('ApiTokens', 'U') IS NOT NULL
    UPDATE token SET RevokedAt = @Now
    FROM ApiTokens token JOIN AppLoginUsers u ON u.TenantId = token.TenantId AND u.Username = token.Username
    JOIN @Due d ON d.EmployeeId = u.EmployeeId AND d.TenantId = u.TenantId
    WHERE token.RevokedAt IS NULL;
IF OBJECT_ID('SystemUsers', 'U') IS NOT NULL
    UPDATE u SET IsActive = 0
    FROM SystemUsers u JOIN @Due d ON d.EmployeeId = u.EmployeeId AND d.TenantId = u.TenantId
    WHERE u.IsActive = 1;
UPDATE s SET ClosedAtUtc = @Now
FROM EndServiceAccessSchedules s JOIN @Due d ON d.EndServiceId = s.EndServiceId AND d.CompanyId = s.CompanyId;
COMMIT TRANSACTION;
""";

    public static async Task CloseDueAsync(ApplicationDbContext db, DateTimeOffset now)
    {
        if (!await IsReadyAsync(db)) return;
        await HrmsDatabase.ExecuteAsync(db, CloseDueSql,
            command => HrmsDatabase.AddParameter(command, "@Now", now.UtcDateTime));
    }
}
