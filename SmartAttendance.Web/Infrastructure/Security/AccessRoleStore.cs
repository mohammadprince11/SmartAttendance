using System.Data.Common;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// Self-healing store for the unified Access Roles model (Kayan-parity — see
/// docs/kayan-access-roles-study.md). A role is a reusable object of one of five
/// types (Pages, Data, SensitiveFields, SelfService, Reports); it carries grants
/// (a typed payload) and is assigned to many users. Follows the Hrms store
/// pattern: idempotent CREATE TABLE, raw ADO, zero EF migrations.
/// </summary>
public static class AccessRoleStore
{
    public const string TypePages = "Pages";
    public const string TypeData = "Data";
    public const string TypeSensitiveFields = "SensitiveFields";
    public const string TypeSelfService = "SelfService";
    public const string TypeReports = "Reports";

    public static readonly IReadOnlyList<string> RoleTypes = new[]
    {
        TypePages, TypeData, TypeSensitiveFields, TypeSelfService, TypeReports,
    };

    public sealed class AccessRole
    {
        public int Id { get; set; }
        public string RoleType { get; set; } = TypePages;
        public string NameAr { get; set; } = string.Empty;
        public string? NameEn { get; set; }
        public string? Note { get; set; }
        public bool IsActive { get; set; } = true;
        public int AffectedUsers { get; set; }
    }

    /// <summary>A single grant line for a role: a key plus an optional JSON payload.</summary>
    public sealed class AccessRoleGrant
    {
        public string GrantKey { get; set; } = string.Empty;
        public string? Payload { get; set; }
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('AccessRoles', 'U') IS NULL
BEGIN
    CREATE TABLE AccessRoles
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RoleType nvarchar(20) NOT NULL,
        NameAr nvarchar(150) NOT NULL,
        NameEn nvarchar(150) NULL,
        Note nvarchar(500) NULL,
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME())
    );
    CREATE INDEX IX_AccessRoles_Type ON AccessRoles (RoleType);
END;

IF OBJECT_ID('AccessRoleGrants', 'U') IS NULL
BEGIN
    CREATE TABLE AccessRoleGrants
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RoleId int NOT NULL,
        GrantKey nvarchar(200) NOT NULL,
        Payload nvarchar(max) NULL
    );
    CREATE INDEX IX_AccessRoleGrants_Role ON AccessRoleGrants (RoleId);
END;

IF OBJECT_ID('UserAccessRoles', 'U') IS NULL
BEGIN
    CREATE TABLE UserAccessRoles
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        SystemUserId int NOT NULL,
        RoleId int NOT NULL
    );
    CREATE INDEX IX_UserAccessRoles_Role ON UserAccessRoles (RoleId);
    CREATE INDEX IX_UserAccessRoles_User ON UserAccessRoles (SystemUserId);
END;
""");
    }

    /// <summary>Roles of a type with their assigned-user counts (for the listing).</summary>
    public static async Task<List<AccessRole>> ListAsync(ApplicationDbContext dbContext, string roleType)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT r.*, (SELECT COUNT(*) FROM UserAccessRoles u WHERE u.RoleId = r.Id) AS AffectedUsers
FROM AccessRoles r
WHERE r.RoleType = @RoleType
ORDER BY r.Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@RoleType", roleType),
            ReadRole);
    }

    public static async Task<Dictionary<string, int>> CountsAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT RoleType, COUNT(*) AS Cnt FROM AccessRoles GROUP BY RoleType;",
            command => { },
            reader => new
            {
                Type = HrmsDatabase.GetString(reader, "RoleType"),
                Count = HrmsDatabase.GetInt(reader, "Cnt"),
            });
        return rows.ToDictionary(r => r.Type, r => r.Count, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<AccessRole?> GetAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT r.*, (SELECT COUNT(*) FROM UserAccessRoles u WHERE u.RoleId = r.Id) AS AffectedUsers
FROM AccessRoles r WHERE r.Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            ReadRole);
        return rows.FirstOrDefault();
    }

    public static async Task<int> SaveAsync(ApplicationDbContext dbContext, AccessRole role)
    {
        await EnsureAsync(dbContext);

        if (role.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE AccessRoles SET NameAr = @NameAr, NameEn = @NameEn, Note = @Note, IsActive = @IsActive
WHERE Id = @Id;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", role.Id);
                    AddRoleParameters(command, role);
                });
            return role.Id;
        }

        return await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
INSERT INTO AccessRoles (RoleType, NameAr, NameEn, Note, IsActive)
VALUES (@RoleType, @NameAr, @NameEn, @Note, @IsActive);
SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@RoleType", role.RoleType);
                AddRoleParameters(command, role);
            });
    }

    public static async Task ToggleActiveAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE AccessRoles SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END WHERE Id = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    public static async Task DeleteAsync(ApplicationDbContext dbContext, int id)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
DELETE FROM AccessRoleGrants WHERE RoleId = @Id;
DELETE FROM UserAccessRoles WHERE RoleId = @Id;
DELETE FROM AccessRoles WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    // ---- Grants ------------------------------------------------------------

    public static async Task<List<AccessRoleGrant>> GetGrantsAsync(ApplicationDbContext dbContext, int roleId)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT GrantKey, Payload FROM AccessRoleGrants WHERE RoleId = @RoleId ORDER BY Id;",
            command => HrmsDatabase.AddParameter(command, "@RoleId", roleId),
            reader => new AccessRoleGrant
            {
                GrantKey = HrmsDatabase.GetString(reader, "GrantKey"),
                Payload = NullIfEmpty(HrmsDatabase.GetString(reader, "Payload")),
            });
    }

    public static async Task ReplaceGrantsAsync(
        ApplicationDbContext dbContext, int roleId, IEnumerable<AccessRoleGrant> grants)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM AccessRoleGrants WHERE RoleId = @RoleId;",
            command => HrmsDatabase.AddParameter(command, "@RoleId", roleId));

        foreach (var grant in grants)
        {
            if (string.IsNullOrWhiteSpace(grant.GrantKey))
            {
                continue;
            }

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "INSERT INTO AccessRoleGrants (RoleId, GrantKey, Payload) VALUES (@RoleId, @GrantKey, @Payload);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@RoleId", roleId);
                    HrmsDatabase.AddParameter(command, "@GrantKey", grant.GrantKey);
                    HrmsDatabase.AddParameter(command, "@Payload", (object?)grant.Payload ?? DBNull.Value);
                });
        }
    }

    // ---- User assignment ---------------------------------------------------

    public static async Task<List<int>> GetAssignedUserIdsAsync(ApplicationDbContext dbContext, int roleId)
    {
        await EnsureAsync(dbContext);
        return await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT SystemUserId FROM UserAccessRoles WHERE RoleId = @RoleId;",
            command => HrmsDatabase.AddParameter(command, "@RoleId", roleId),
            reader => HrmsDatabase.GetInt(reader, "SystemUserId"));
    }

    public static async Task ReplaceAssignedUsersAsync(
        ApplicationDbContext dbContext, int roleId, IEnumerable<int> userIds)
    {
        await EnsureAsync(dbContext);
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM UserAccessRoles WHERE RoleId = @RoleId;",
            command => HrmsDatabase.AddParameter(command, "@RoleId", roleId));

        foreach (var userId in userIds.Where(u => u > 0).Distinct())
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "INSERT INTO UserAccessRoles (SystemUserId, RoleId) VALUES (@UserId, @RoleId);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@UserId", userId);
                    HrmsDatabase.AddParameter(command, "@RoleId", roleId);
                });
        }
    }

    // ---- Helpers -----------------------------------------------------------

    private static AccessRole ReadRole(DbDataReader reader) => new()
    {
        Id = HrmsDatabase.GetInt(reader, "Id"),
        RoleType = HrmsDatabase.GetString(reader, "RoleType"),
        NameAr = HrmsDatabase.GetString(reader, "NameAr"),
        NameEn = NullIfEmpty(HrmsDatabase.GetString(reader, "NameEn")),
        Note = NullIfEmpty(HrmsDatabase.GetString(reader, "Note")),
        IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
        AffectedUsers = HrmsDatabase.GetInt(reader, "AffectedUsers"),
    };

    private static void AddRoleParameters(DbCommand command, AccessRole role)
    {
        HrmsDatabase.AddParameter(command, "@NameAr", role.NameAr);
        HrmsDatabase.AddParameter(command, "@NameEn", (object?)role.NameEn ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@Note", (object?)role.Note ?? DBNull.Value);
        HrmsDatabase.AddParameter(command, "@IsActive", role.IsActive ? 1 : 0);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
