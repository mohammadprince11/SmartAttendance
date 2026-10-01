using System.Data;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Infrastructure.Platform;

public static class PlatformPortalStore
{
    private const string BootstrapUsernameVariable = "ZYNORA_PLATFORM_OWNER_USERNAME";
    private const string BootstrapPasswordVariable = "ZYNORA_PLATFORM_OWNER_PASSWORD";

    public static readonly IReadOnlyDictionary<string, string> ModuleCatalog =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CoreHR"] = "الموارد البشرية",
            ["Attendance"] = "الحضور والانصراف",
            ["Payroll"] = "الرواتب",
            ["SelfService"] = "الخدمة الذاتية",
            ["Performance"] = "إدارة الأداء",
            ["Mobile"] = "تطبيق الموظف",
            ["PeopleAI"] = "ذكاء الأشخاص"
        };

    public sealed record PlatformOwnerIdentity(
        int Id,
        string Username,
        string DisplayName,
        string PasswordHash,
        string PasswordSalt,
        bool IsActive);

    public sealed record TenantSummary(
        int Id,
        string Code,
        string Name,
        bool IsActive,
        string PlanCode,
        string LicenseStatus,
        DateTime StartsAtUtc,
        DateTime? ExpiresAtUtc,
        DateTime? GraceEndsAtUtc,
        int MaxCompanies,
        int MaxEmployees,
        int MaxDevices,
        string EnabledModulesCsv,
        int CompanyCount,
        int EmployeeCount,
        int DeviceCount,
        DateTime CreatedAt,
        byte[] Version)
    {
        public string EffectiveStatus => PlatformLicensePolicy.DisplayStatus(
            LicenseStatus,
            ExpiresAtUtc,
            GraceEndsAtUtc,
            DateTime.UtcNow);

        public IReadOnlyList<string> EnabledModules => EnabledModulesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public string VersionToken => Convert.ToBase64String(Version);
    }

    public sealed record CreateTenantCommand(
        string Name,
        string AdminUsername,
        string AdminPassword,
        string PlanCode,
        string LicenseStatus,
        DateTime StartsAtUtc,
        DateTime? ExpiresAtUtc,
        DateTime? GraceEndsAtUtc,
        int MaxCompanies,
        int MaxEmployees,
        int MaxDevices,
        IReadOnlyCollection<string> EnabledModules);

    public sealed record UpdateLicenseCommand(
        int TenantId,
        string PlanCode,
        string LicenseStatus,
        DateTime StartsAtUtc,
        DateTime? ExpiresAtUtc,
        DateTime? GraceEndsAtUtc,
        int MaxCompanies,
        int MaxEmployees,
        int MaxDevices,
        IReadOnlyCollection<string> EnabledModules,
        byte[] ExpectedVersion);

    public static async Task VerifySchemaAsync(ApplicationDbContext db)
    {
        var missing = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT RequiredName
FROM (VALUES
    (N'PlatformOwners'),
    (N'TenantLicenses'),
    (N'PlatformAuditEvents')
) required(RequiredName)
WHERE OBJECT_ID(N'dbo.' + RequiredName, 'U') IS NULL;
""",
            null,
            reader => HrmsDatabase.GetString(reader, "RequiredName"));

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "مخطط بوابة مالك المنصة غير مكتمل. الجداول المفقودة: " +
                string.Join(", ", missing));
        }
    }

    public static async Task EnsureBootstrapOwnerAsync(ApplicationDbContext db)
    {
        var count = await HrmsDatabase.ScalarAsync<int>(
            db,
            "SELECT COUNT(*) FROM dbo.PlatformOwners;");

        if (count > 0)
        {
            return;
        }

        var username = Environment.GetEnvironmentVariable(BootstrapUsernameVariable)?.Trim();
        var password = Environment.GetEnvironmentVariable(BootstrapPasswordVariable);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (password.Length < 12)
        {
            throw new InvalidOperationException(
                $"{BootstrapPasswordVariable} must contain at least 12 characters.");
        }

        var salt = SimplePasswordHasher.CreateSalt();
        var hash = SimplePasswordHasher.HashPassword(password, salt);
        var normalized = username.ToUpperInvariant();

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var current = await HrmsDatabase.ScalarAsync<int>(db, "SELECT COUNT(*) FROM dbo.PlatformOwners WITH (UPDLOCK, HOLDLOCK);");
        if (current == 0)
        {
            await HrmsDatabase.ExecuteAsync(
                db,
                """
INSERT INTO dbo.PlatformOwners
    (Username, NormalizedUsername, DisplayName, PasswordHash, PasswordSalt, IsActive, CreatedAtUtc)
VALUES
    (@Username, @NormalizedUsername, @DisplayName, @PasswordHash, @PasswordSalt, 1, SYSUTCDATETIME());

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
VALUES
    (N'System', N'BootstrapOwnerCreated', N'PlatformOwner', @Username,
     N'Initial platform owner created from protected environment values.', NULL, SYSUTCDATETIME());
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Username", username);
                    HrmsDatabase.AddParameter(command, "@NormalizedUsername", normalized);
                    HrmsDatabase.AddParameter(command, "@DisplayName", username);
                    HrmsDatabase.AddParameter(command, "@PasswordHash", hash);
                    HrmsDatabase.AddParameter(command, "@PasswordSalt", salt);
                });
        }

        await transaction.CommitAsync();
    }

    public static async Task<PlatformOwnerIdentity?> FindOwnerAsync(
        ApplicationDbContext db,
        string username)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP (1) Id, Username, DisplayName, PasswordHash, PasswordSalt, IsActive
FROM dbo.PlatformOwners
WHERE NormalizedUsername = @NormalizedUsername;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@NormalizedUsername",
                username.Trim().ToUpperInvariant()),
            reader => new PlatformOwnerIdentity(
                HrmsDatabase.GetInt(reader, "Id"),
                HrmsDatabase.GetString(reader, "Username"),
                HrmsDatabase.GetString(reader, "DisplayName"),
                HrmsDatabase.GetString(reader, "PasswordHash"),
                HrmsDatabase.GetString(reader, "PasswordSalt"),
                HrmsDatabase.GetBool(reader, "IsActive")));

        return rows.SingleOrDefault();
    }

    public static Task RecordOwnerLoginAsync(
        ApplicationDbContext db,
        int ownerId,
        string username,
        string? ipAddress) =>
        HrmsDatabase.ExecuteAsync(
            db,
            """
UPDATE dbo.PlatformOwners
SET LastLoginAtUtc = SYSUTCDATETIME()
WHERE Id = @OwnerId;

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
VALUES
    (@Username, N'OwnerLogin', N'PlatformOwner', CONVERT(nvarchar(40), @OwnerId),
     N'Platform owner signed in.', @IpAddress, SYSUTCDATETIME());
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@OwnerId", ownerId);
                HrmsDatabase.AddParameter(command, "@Username", username);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
            });

    public static Task<List<TenantSummary>> ListTenantsAsync(
        ApplicationDbContext db,
        string? search = null)
    {
        var normalizedSearch = string.IsNullOrWhiteSpace(search)
            ? null
            : search.Trim()[..Math.Min(search.Trim().Length, 100)];

        return HrmsDatabase.QueryAsync(
            db,
            TenantSummarySql +
            """
 WHERE (@Search IS NULL
        OR t.Code LIKE @SearchPattern ESCAPE N'~'
        OR t.Name LIKE @SearchPattern ESCAPE N'~'
        OR EXISTS (
            SELECT 1
            FROM dbo.Companies searchCompany
            WHERE searchCompany.TenantId = t.Id
              AND searchCompany.IsDeleted = 0
              AND (searchCompany.Name LIKE @SearchPattern ESCAPE N'~'
                   OR searchCompany.Code LIKE @SearchPattern ESCAPE N'~')))
 ORDER BY t.CreatedAt DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Search", normalizedSearch);
                HrmsDatabase.AddParameter(
                    command,
                    "@SearchPattern",
                    normalizedSearch is null ? null : $"%{EscapeLikePattern(normalizedSearch)}%");
            },
            MapTenantSummary);
    }

    public static async Task<TenantSummary?> GetTenantAsync(ApplicationDbContext db, int tenantId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            TenantSummarySql + " WHERE t.Id = @TenantId;",
            command => HrmsDatabase.AddParameter(command, "@TenantId", tenantId),
            MapTenantSummary);

        return rows.SingleOrDefault();
    }

    public static async Task<(int TenantId, string Code)> CreateTenantAsync(
        ApplicationDbContext db,
        CreateTenantCommand input,
        string actorUsername,
        string? ipAddress)
    {
        var modules = NormalizeModules(input.EnabledModules);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var existingCodes = await HrmsDatabase.QueryAsync(
            db,
            "SELECT Code FROM dbo.Tenants WITH (UPDLOCK, HOLDLOCK);",
            null,
            reader => HrmsDatabase.GetString(reader, "Code"));
        var used = existingCodes.ToHashSet(StringComparer.Ordinal);
        var code = Enumerable.Range(1, 9999)
            .Select(number => number.ToString("0000"))
            .FirstOrDefault(candidate => !used.Contains(candidate))
            ?? throw new InvalidOperationException("لا توجد أكواد منظومات متاحة ضمن نطاق الأربعة أرقام.");

        var tenantId = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
INSERT INTO dbo.Tenants (Code, Name, IsActive, CreatedAt, UpdatedAt, IsDeleted)
VALUES (@Code, @Name, 1, SYSUTCDATETIME(), NULL, 0);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Code", code);
                HrmsDatabase.AddParameter(command, "@Name", input.Name.Trim());
            });

        var salt = SimplePasswordHasher.CreateSalt();
        var hash = SimplePasswordHasher.HashPassword(input.AdminPassword, salt);

        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO dbo.TenantLicenses
    (TenantId, PlanCode, Status, StartsAtUtc, ExpiresAtUtc, GraceEndsAtUtc,
     MaxCompanies, MaxEmployees, MaxDevices, EnabledModulesCsv, CreatedAtUtc, UpdatedAtUtc)
VALUES
    (@TenantId, @PlanCode, @Status, @StartsAtUtc, @ExpiresAtUtc, @GraceEndsAtUtc,
     @MaxCompanies, @MaxEmployees, @MaxDevices, @Modules, SYSUTCDATETIME(), NULL);

INSERT INTO dbo.AppLoginUsers
    (TenantId, EmployeeId, Username, PasswordHash, PasswordSalt, Role, IsActive,
     FailedLoginAttempts, SecurityStamp, MustChangePassword, CreatedAt)
VALUES
    (@TenantId, NULL, @AdminUsername, @PasswordHash, @PasswordSalt, N'Admin', 1,
     0, REPLACE(CONVERT(nvarchar(36), NEWID()), N'-', N''), 1, SYSUTCDATETIME());

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
VALUES
    (@Actor, N'TenantCreated', N'Tenant', @Code,
     CONCAT(N'Created tenant and initial administrator. Plan=', @PlanCode),
     @IpAddress, SYSUTCDATETIME());
""",
            command =>
            {
                AddLicenseParameters(command, tenantId, input.PlanCode, input.LicenseStatus,
                    input.StartsAtUtc, input.ExpiresAtUtc, input.GraceEndsAtUtc,
                    input.MaxCompanies, input.MaxEmployees, input.MaxDevices, modules);
                HrmsDatabase.AddParameter(command, "@AdminUsername", input.AdminUsername.Trim());
                HrmsDatabase.AddParameter(command, "@PasswordHash", hash);
                HrmsDatabase.AddParameter(command, "@PasswordSalt", salt);
                HrmsDatabase.AddParameter(command, "@Actor", actorUsername);
                HrmsDatabase.AddParameter(command, "@Code", code);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
            });

        await transaction.CommitAsync();
        return (tenantId, code);
    }

    public static async Task<bool> UpdateLicenseAsync(
        ApplicationDbContext db,
        UpdateLicenseCommand input,
        string actorUsername,
        string? ipAddress)
    {
        var modules = NormalizeModules(input.EnabledModules);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var affected = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
UPDATE dbo.TenantLicenses
SET PlanCode = @PlanCode,
    Status = @Status,
    StartsAtUtc = @StartsAtUtc,
    ExpiresAtUtc = @ExpiresAtUtc,
    GraceEndsAtUtc = @GraceEndsAtUtc,
    MaxCompanies = @MaxCompanies,
    MaxEmployees = @MaxEmployees,
    MaxDevices = @MaxDevices,
    EnabledModulesCsv = @Modules,
    UpdatedAtUtc = SYSUTCDATETIME()
WHERE TenantId = @TenantId AND Version = @ExpectedVersion;
SELECT @@ROWCOUNT;
""",
            command =>
            {
                AddLicenseParameters(command, input.TenantId, input.PlanCode, input.LicenseStatus,
                    input.StartsAtUtc, input.ExpiresAtUtc, input.GraceEndsAtUtc,
                    input.MaxCompanies, input.MaxEmployees, input.MaxDevices, modules);
                HrmsDatabase.AddParameter(command, "@ExpectedVersion", input.ExpectedVersion);
            });

        if (affected != 1)
        {
            await transaction.RollbackAsync();
            return false;
        }

        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
SELECT @Actor, N'LicenseUpdated', N'Tenant', Code,
       CONCAT(N'License updated. Plan=', @PlanCode, N'; Status=', @Status),
       @IpAddress, SYSUTCDATETIME()
FROM dbo.Tenants WHERE Id = @TenantId;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Actor", actorUsername);
                HrmsDatabase.AddParameter(command, "@PlanCode", input.PlanCode);
                HrmsDatabase.AddParameter(command, "@Status", input.LicenseStatus);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
                HrmsDatabase.AddParameter(command, "@TenantId", input.TenantId);
            });

        await transaction.CommitAsync();
        return true;
    }

    public static async Task<bool> SetTenantActiveAsync(
        ApplicationDbContext db,
        int tenantId,
        bool isActive,
        string actorUsername,
        string? ipAddress)
    {
        var affected = await HrmsDatabase.ScalarAsync<int>(
            db,
            """
UPDATE dbo.Tenants
SET IsActive = @IsActive, UpdatedAt = SYSUTCDATETIME()
WHERE Id = @TenantId AND IsDeleted = 0;

INSERT INTO dbo.PlatformAuditEvents
    (ActorUsername, ActionCode, TargetType, TargetKey, Details, IpAddress, CreatedAtUtc)
SELECT @Actor, CASE WHEN @IsActive = 1 THEN N'TenantActivated' ELSE N'TenantSuspended' END,
       N'Tenant', Code, N'Tenant active flag changed.', @IpAddress, SYSUTCDATETIME()
FROM dbo.Tenants WHERE Id = @TenantId AND @@ROWCOUNT = 1;
SELECT @@ROWCOUNT;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@TenantId", tenantId);
                HrmsDatabase.AddParameter(command, "@IsActive", isActive);
                HrmsDatabase.AddParameter(command, "@Actor", actorUsername);
                HrmsDatabase.AddParameter(command, "@IpAddress", ipAddress);
            });

        return affected > 0;
    }

    public static async Task<bool> IsTenantAccessAllowedAsync(
        ApplicationDbContext db,
        int tenantId,
        DateTime utcNow)
    {
        var rows = await HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP (1) t.IsActive, l.Status, l.StartsAtUtc, l.ExpiresAtUtc, l.GraceEndsAtUtc
FROM dbo.Tenants t
INNER JOIN dbo.TenantLicenses l ON l.TenantId = t.Id
WHERE t.Id = @TenantId AND t.IsDeleted = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@TenantId", tenantId),
            reader => new
            {
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                Starts = HrmsDatabase.GetDateTime(reader, "StartsAtUtc") ?? DateTime.MaxValue,
                Expires = HrmsDatabase.GetDateTime(reader, "ExpiresAtUtc"),
                Grace = HrmsDatabase.GetDateTime(reader, "GraceEndsAtUtc")
            });

        var license = rows.SingleOrDefault();
        return license is not null && license.IsActive && PlatformLicensePolicy.AllowsAccess(
            license.Status, license.Starts, license.Expires, license.Grace, utcNow);
    }

    private const string TenantSummarySql =
        """
SELECT t.Id, t.Code, t.Name, t.IsActive, t.CreatedAt,
       l.PlanCode, l.Status, l.StartsAtUtc, l.ExpiresAtUtc, l.GraceEndsAtUtc,
       l.MaxCompanies, l.MaxEmployees, l.MaxDevices, l.EnabledModulesCsv, l.Version,
       (SELECT COUNT(*) FROM dbo.Companies c WHERE c.TenantId = t.Id AND c.IsDeleted = 0) AS CompanyCount,
       (SELECT COUNT(*)
          FROM dbo.Employees e
          LEFT JOIN dbo.Branches employeeBranch ON employeeBranch.Id = e.BranchId
          INNER JOIN dbo.Companies c ON c.Id = COALESCE(e.CompanyId, employeeBranch.CompanyId)
          WHERE c.TenantId = t.Id AND e.IsDeleted = 0) AS EmployeeCount,
       (SELECT COUNT(*)
          FROM dbo.Devices d
          INNER JOIN dbo.Branches b ON b.Id = d.BranchId
          INNER JOIN dbo.Companies c ON c.Id = b.CompanyId
          WHERE c.TenantId = t.Id AND d.IsDeleted = 0 AND b.IsDeleted = 0) AS DeviceCount
FROM dbo.Tenants t
INNER JOIN dbo.TenantLicenses l ON l.TenantId = t.Id
""";

    private static TenantSummary MapTenantSummary(System.Data.Common.DbDataReader reader) =>
        new(
            HrmsDatabase.GetInt(reader, "Id"),
            HrmsDatabase.GetString(reader, "Code"),
            HrmsDatabase.GetString(reader, "Name"),
            HrmsDatabase.GetBool(reader, "IsActive"),
            HrmsDatabase.GetString(reader, "PlanCode"),
            HrmsDatabase.GetString(reader, "Status"),
            HrmsDatabase.GetDateTime(reader, "StartsAtUtc") ?? DateTime.MinValue,
            HrmsDatabase.GetDateTime(reader, "ExpiresAtUtc"),
            HrmsDatabase.GetDateTime(reader, "GraceEndsAtUtc"),
            HrmsDatabase.GetInt(reader, "MaxCompanies"),
            HrmsDatabase.GetInt(reader, "MaxEmployees"),
            HrmsDatabase.GetInt(reader, "MaxDevices"),
            HrmsDatabase.GetString(reader, "EnabledModulesCsv"),
            HrmsDatabase.GetInt(reader, "CompanyCount"),
            HrmsDatabase.GetInt(reader, "EmployeeCount"),
            HrmsDatabase.GetInt(reader, "DeviceCount"),
            HrmsDatabase.GetDateTime(reader, "CreatedAt") ?? DateTime.MinValue,
            (byte[])reader["Version"]);

    private static string NormalizeModules(IEnumerable<string> modules) =>
        string.Join(',', modules
            .Where(module => ModuleCatalog.ContainsKey(module))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(module => module, StringComparer.OrdinalIgnoreCase));

    private static string EscapeLikePattern(string value) =>
        value
            .Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal)
            .Replace("_", "~_", StringComparison.Ordinal)
            .Replace("[", "~[", StringComparison.Ordinal);

    private static void AddLicenseParameters(
        System.Data.Common.DbCommand command,
        int tenantId,
        string planCode,
        string status,
        DateTime startsAtUtc,
        DateTime? expiresAtUtc,
        DateTime? graceEndsAtUtc,
        int maxCompanies,
        int maxEmployees,
        int maxDevices,
        string modules)
    {
        HrmsDatabase.AddParameter(command, "@TenantId", tenantId);
        HrmsDatabase.AddParameter(command, "@PlanCode", planCode.Trim());
        HrmsDatabase.AddParameter(command, "@Status", status);
        HrmsDatabase.AddParameter(command, "@StartsAtUtc", startsAtUtc);
        HrmsDatabase.AddParameter(command, "@ExpiresAtUtc", expiresAtUtc);
        HrmsDatabase.AddParameter(command, "@GraceEndsAtUtc", graceEndsAtUtc);
        HrmsDatabase.AddParameter(command, "@MaxCompanies", maxCompanies);
        HrmsDatabase.AddParameter(command, "@MaxEmployees", maxEmployees);
        HrmsDatabase.AddParameter(command, "@MaxDevices", maxDevices);
        HrmsDatabase.AddParameter(command, "@Modules", modules);
    }
}
