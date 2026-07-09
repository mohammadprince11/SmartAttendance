using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Security;

public static class LoginDatabase
{
    public const string DefaultAdminUsername = "admin";
    public const string DefaultAdminPassword = "Admin@12345";
    public const string DefaultEmployeeUsername = "employee";
    public const string DefaultEmployeePassword = "Employee@12345";

    public static async Task EnsureCreatedAsync(ApplicationDbContext dbContext)
    {
        await HrmsDatabase.EnsureCreatedAsync(dbContext);

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('AppLoginUsers', 'U') IS NULL
BEGIN
    CREATE TABLE AppLoginUsers
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NULL,
        Username nvarchar(100) NOT NULL,
        PasswordHash nvarchar(200) NOT NULL,
        PasswordSalt nvarchar(200) NOT NULL,
        Role nvarchar(50) NOT NULL,
        IsActive bit NOT NULL DEFAULT(1),
        LastLoginAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        UpdatedAt datetime2 NULL
    );

    CREATE UNIQUE INDEX IX_AppLoginUsers_Username ON AppLoginUsers(Username);
END;
""");

        var totalUsers = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "SELECT COUNT(*) FROM AppLoginUsers");

        if (totalUsers == 0)
        {
            var salt = SimplePasswordHasher.CreateSalt();
            var hash = SimplePasswordHasher.HashPassword(DefaultAdminPassword, salt);

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO AppLoginUsers
(EmployeeId, Username, PasswordHash, PasswordSalt, Role, IsActive, CreatedAt)
VALUES
(NULL, @Username, @PasswordHash, @PasswordSalt, 'Admin', 1, SYSUTCDATETIME());

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName)
VALUES ('AppLoginUsers', 'admin', 'Seed Default Admin User', 'Default admin user created', 'System');
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Username", DefaultAdminUsername);
                    HrmsDatabase.AddParameter(command, "@PasswordHash", hash);
                    HrmsDatabase.AddParameter(command, "@PasswordSalt", salt);
                });
        }

        await EnsureDefaultEmployeeUserAsync(dbContext);
    }

    private static async Task EnsureDefaultEmployeeUserAsync(ApplicationDbContext dbContext)
    {
        var employeeId = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            """
SELECT TOP 1 Id
FROM Employees
WHERE IsDeleted = 0 AND IsActive = 1
ORDER BY
    CASE
        WHEN EmployeeNo = '11230' THEN 0
        WHEN FullName LIKE N'%Ù…Ø­Ù…Ø¯ Ø¹Ù„ÙŠ Ø²ÙŠØ¯Ø§Ù†%' THEN 1
        ELSE 2
    END,
    Id;
""");

        if (employeeId <= 0)
        {
            return;
        }

        var existing = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "SELECT COUNT(*) FROM AppLoginUsers WHERE Username = @Username",
            command => HrmsDatabase.AddParameter(command, "@Username", DefaultEmployeeUsername));

        if (existing == 0)
        {
            var salt = SimplePasswordHasher.CreateSalt();
            var hash = SimplePasswordHasher.HashPassword(DefaultEmployeePassword, salt);

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO AppLoginUsers
(EmployeeId, Username, PasswordHash, PasswordSalt, Role, IsActive, CreatedAt)
VALUES
(@EmployeeId, @Username, @PasswordHash, @PasswordSalt, 'Employee', 1, SYSUTCDATETIME());

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName)
VALUES ('AppLoginUsers', @Username, 'Seed Official Employee User', 'Official employee portal account created', 'System');
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@Username", DefaultEmployeeUsername);
                    HrmsDatabase.AddParameter(command, "@PasswordHash", hash);
                    HrmsDatabase.AddParameter(command, "@PasswordSalt", salt);
                });
        }
        else
        {
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE AppLoginUsers
SET EmployeeId = @EmployeeId,
    UpdatedAt = SYSUTCDATETIME()
WHERE Username = @Username
  AND (EmployeeId IS NULL OR EmployeeId = 0);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@Username", DefaultEmployeeUsername);
                });
        }
    }

    public static async Task<LoginUser?> GetByUsernameAsync(ApplicationDbContext dbContext, string username)
    {
        var users = await HrmsDatabase.QueryAsync(
            dbContext,
            """
SELECT TOP 1
    u.Id,
    ISNULL(u.EmployeeId, 0) AS EmployeeId,
    u.Username,
    u.PasswordHash,
    u.PasswordSalt,
    u.Role,
    u.IsActive,
    ISNULL(e.FullName, '') AS EmployeeName
FROM AppLoginUsers u
LEFT JOIN Employees e ON u.EmployeeId = e.Id
WHERE u.Username = @Username;
""",
            command => HrmsDatabase.AddParameter(command, "@Username", username),
            reader => new LoginUser
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId") == 0 ? null : HrmsDatabase.GetInt(reader, "EmployeeId"),
                Username = HrmsDatabase.GetString(reader, "Username"),
                PasswordHash = HrmsDatabase.GetString(reader, "PasswordHash"),
                PasswordSalt = HrmsDatabase.GetString(reader, "PasswordSalt"),
                Role = HrmsDatabase.GetString(reader, "Role"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                EmployeeName = HrmsDatabase.GetString(reader, "EmployeeName")
            });

        return users.FirstOrDefault();
    }

    public static async Task UpdateLastLoginAsync(ApplicationDbContext dbContext, int id)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "UPDATE AppLoginUsers SET LastLoginAt = SYSUTCDATETIME() WHERE Id = @Id",
            command => HrmsDatabase.AddParameter(command, "@Id", id));
    }

    public class LoginUser
    {
        public int Id { get; set; }

        public int? EmployeeId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;

        public string PasswordSalt { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public string EmployeeName { get; set; } = string.Empty;
    }
}
