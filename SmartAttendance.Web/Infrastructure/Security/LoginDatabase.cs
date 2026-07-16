using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Security;

public static class LoginDatabase
{
    public const string DefaultAdminUsername = "admin";
    public const string DefaultEmployeeUsername = "employee";
    public const int MaximumFailedLoginAttempts = 5;

    public static readonly TimeSpan LockoutDuration =
        TimeSpan.FromMinutes(15);

    private const string BootstrapAdminPasswordVariable =
        "ZYNORA_BOOTSTRAP_ADMIN_PASSWORD";

    private const string BootstrapEmployeePasswordVariable =
        "ZYNORA_BOOTSTRAP_EMPLOYEE_PASSWORD";

    public static async Task EnsureCreatedAsync(
        ApplicationDbContext dbContext)
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
        FailedLoginAttempts int NOT NULL DEFAULT(0),
        LockoutEndUtc datetime2 NULL,
        LastFailedLoginAt datetime2 NULL,
        LastLoginAt datetime2 NULL,
        PasswordChangedAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        UpdatedAt datetime2 NULL
    );

    CREATE UNIQUE INDEX IX_AppLoginUsers_Username
        ON AppLoginUsers(Username);
END;

IF COL_LENGTH('AppLoginUsers', 'FailedLoginAttempts') IS NULL
    ALTER TABLE AppLoginUsers
        ADD FailedLoginAttempts int NOT NULL DEFAULT(0);

IF COL_LENGTH('AppLoginUsers', 'LockoutEndUtc') IS NULL
    ALTER TABLE AppLoginUsers ADD LockoutEndUtc datetime2 NULL;

IF COL_LENGTH('AppLoginUsers', 'LastFailedLoginAt') IS NULL
    ALTER TABLE AppLoginUsers ADD LastFailedLoginAt datetime2 NULL;

IF COL_LENGTH('AppLoginUsers', 'PasswordChangedAt') IS NULL
    ALTER TABLE AppLoginUsers ADD PasswordChangedAt datetime2 NULL;
""");

        var totalUsers = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "SELECT COUNT(*) FROM AppLoginUsers");

        if (totalUsers == 0)
        {
            var bootstrapPassword = GetRequiredBootstrapPassword(
                BootstrapAdminPasswordVariable);
            var salt = SimplePasswordHasher.CreateSalt();
            var hash = SimplePasswordHasher.HashPassword(
                bootstrapPassword,
                salt);

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO AppLoginUsers
(
    EmployeeId,
    Username,
    PasswordHash,
    PasswordSalt,
    Role,
    IsActive,
    PasswordChangedAt,
    CreatedAt
)
VALUES
(
    NULL,
    @Username,
    @PasswordHash,
    @PasswordSalt,
    'Admin',
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);

INSERT INTO AuditLogs
(EntityName, EntityId, Action, NewValues, UserName)
VALUES
(
    'AppLoginUsers',
    'admin',
    'Seed Bootstrap Admin User',
    'Bootstrap administrator created from a protected environment value',
    'System'
);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(
                        command,
                        "@Username",
                        DefaultAdminUsername);
                    HrmsDatabase.AddParameter(
                        command,
                        "@PasswordHash",
                        hash);
                    HrmsDatabase.AddParameter(
                        command,
                        "@PasswordSalt",
                        salt);
                });
        }

        await EnsureDefaultEmployeeUserAsync(dbContext);
    }

    private static async Task EnsureDefaultEmployeeUserAsync(
        ApplicationDbContext dbContext)
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
        WHEN FullName LIKE N'%محمد علي زيدان%' THEN 1
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
            """
SELECT COUNT(*)
FROM AppLoginUsers
WHERE Username = @Username
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@Username",
                DefaultEmployeeUsername));

        if (existing == 0)
        {
            var bootstrapPassword = Environment.GetEnvironmentVariable(
                BootstrapEmployeePasswordVariable);

            if (string.IsNullOrWhiteSpace(bootstrapPassword))
            {
                return;
            }

            var salt = SimplePasswordHasher.CreateSalt();
            var hash = SimplePasswordHasher.HashPassword(
                bootstrapPassword,
                salt);

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
INSERT INTO AppLoginUsers
(
    EmployeeId,
    Username,
    PasswordHash,
    PasswordSalt,
    Role,
    IsActive,
    PasswordChangedAt,
    CreatedAt
)
VALUES
(
    @EmployeeId,
    @Username,
    @PasswordHash,
    @PasswordSalt,
    'Employee',
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
);

INSERT INTO AuditLogs
(EntityName, EntityId, Action, NewValues, UserName)
VALUES
(
    'AppLoginUsers',
    @Username,
    'Seed Bootstrap Employee User',
    'Bootstrap employee account created from a protected environment value',
    'System'
);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(
                        command,
                        "@EmployeeId",
                        employeeId);
                    HrmsDatabase.AddParameter(
                        command,
                        "@Username",
                        DefaultEmployeeUsername);
                    HrmsDatabase.AddParameter(
                        command,
                        "@PasswordHash",
                        hash);
                    HrmsDatabase.AddParameter(
                        command,
                        "@PasswordSalt",
                        salt);
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
                    HrmsDatabase.AddParameter(
                        command,
                        "@EmployeeId",
                        employeeId);
                    HrmsDatabase.AddParameter(
                        command,
                        "@Username",
                        DefaultEmployeeUsername);
                });
        }
    }

    public static async Task<LoginUser?> GetByUsernameAsync(
        ApplicationDbContext dbContext,
        string username)
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
    ISNULL(u.FailedLoginAttempts, 0) AS FailedLoginAttempts,
    u.LockoutEndUtc,
    ISNULL(e.FullName, '') AS EmployeeName
FROM AppLoginUsers u
LEFT JOIN Employees e ON u.EmployeeId = e.Id
WHERE u.Username = @Username;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@Username",
                username),
            reader => new LoginUser
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId =
                    HrmsDatabase.GetInt(reader, "EmployeeId") == 0
                        ? null
                        : HrmsDatabase.GetInt(reader, "EmployeeId"),
                Username = HrmsDatabase.GetString(reader, "Username"),
                PasswordHash = HrmsDatabase.GetString(
                    reader,
                    "PasswordHash"),
                PasswordSalt = HrmsDatabase.GetString(
                    reader,
                    "PasswordSalt"),
                Role = HrmsDatabase.GetString(reader, "Role"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                FailedLoginAttempts = HrmsDatabase.GetInt(
                    reader,
                    "FailedLoginAttempts"),
                LockoutEndUtc = HrmsDatabase.GetDateTime(
                    reader,
                    "LockoutEndUtc"),
                EmployeeName = HrmsDatabase.GetString(
                    reader,
                    "EmployeeName")
            });

        return users.FirstOrDefault();
    }

    public static async Task RecordFailedLoginAsync(
        ApplicationDbContext dbContext,
        LoginUser user,
        string? ipAddress,
        DateTime utcNow)
    {
        var resetExpiredLockout =
            user.LockoutEndUtc.HasValue &&
            user.LockoutEndUtc.Value <= utcNow;

        var nextAttemptCount = resetExpiredLockout
            ? 1
            : user.FailedLoginAttempts + 1;

        DateTime? nextLockoutEndUtc =
            nextAttemptCount >= MaximumFailedLoginAttempts
                ? utcNow.Add(LockoutDuration)
                : null;

        var action = nextLockoutEndUtc.HasValue
            ? "Authentication Account Locked"
            : "Authentication Login Failed";

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE AppLoginUsers
SET FailedLoginAttempts = @FailedLoginAttempts,
    LockoutEndUtc = @LockoutEndUtc,
    LastFailedLoginAt = @UtcNow,
    UpdatedAt = @UtcNow
WHERE Id = @Id;

INSERT INTO AuditLogs
(
    EntityName,
    EntityId,
    Action,
    NewValues,
    UserName,
    IpAddress
)
VALUES
(
    'Authentication',
    @EntityId,
    @Action,
    @Details,
    @UserName,
    @IpAddress
);
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@FailedLoginAttempts",
                    nextAttemptCount);
                HrmsDatabase.AddParameter(
                    command,
                    "@LockoutEndUtc",
                    nextLockoutEndUtc);
                HrmsDatabase.AddParameter(command, "@UtcNow", utcNow);
                HrmsDatabase.AddParameter(command, "@Id", user.Id);
                HrmsDatabase.AddParameter(
                    command,
                    "@EntityId",
                    user.Id.ToString());
                HrmsDatabase.AddParameter(command, "@Action", action);
                HrmsDatabase.AddParameter(
                    command,
                    "@Details",
                    $"Failed attempts: {nextAttemptCount}");
                HrmsDatabase.AddParameter(
                    command,
                    "@UserName",
                    user.Username);
                HrmsDatabase.AddParameter(
                    command,
                    "@IpAddress",
                    NormalizeIpAddress(ipAddress));
            });
    }

    public static async Task RecordUnknownLoginFailureAsync(
        ApplicationDbContext dbContext,
        string username,
        string? ipAddress)
    {
        await WriteAuditAsync(
            dbContext,
            username,
            "Authentication Login Failed",
            "Unknown or unavailable login identity",
            username,
            ipAddress);
    }

    public static async Task RecordRejectedLoginAsync(
        ApplicationDbContext dbContext,
        LoginUser user,
        string reason,
        string? ipAddress)
    {
        await WriteAuditAsync(
            dbContext,
            user.Id.ToString(),
            "Authentication Login Rejected",
            reason,
            user.Username,
            ipAddress);
    }

    public static async Task UpgradePasswordHashAsync(
        ApplicationDbContext dbContext,
        LoginUser user,
        string password,
        string? ipAddress)
    {
        var salt = SimplePasswordHasher.CreateSalt();
        var hash = SimplePasswordHasher.HashPassword(password, salt);
        var utcNow = DateTime.UtcNow;

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE AppLoginUsers
SET PasswordHash = @PasswordHash,
    PasswordSalt = @PasswordSalt,
    PasswordChangedAt = @UtcNow,
    UpdatedAt = @UtcNow
WHERE Id = @Id;

INSERT INTO AuditLogs
(
    EntityName,
    EntityId,
    Action,
    NewValues,
    UserName,
    IpAddress
)
VALUES
(
    'Authentication',
    @EntityId,
    'Authentication Password Hash Upgraded',
    'Password hash upgraded to the current PBKDF2 policy',
    @UserName,
    @IpAddress
);
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@PasswordHash",
                    hash);
                HrmsDatabase.AddParameter(
                    command,
                    "@PasswordSalt",
                    salt);
                HrmsDatabase.AddParameter(command, "@UtcNow", utcNow);
                HrmsDatabase.AddParameter(command, "@Id", user.Id);
                HrmsDatabase.AddParameter(
                    command,
                    "@EntityId",
                    user.Id.ToString());
                HrmsDatabase.AddParameter(
                    command,
                    "@UserName",
                    user.Username);
                HrmsDatabase.AddParameter(
                    command,
                    "@IpAddress",
                    NormalizeIpAddress(ipAddress));
            });

        user.PasswordHash = hash;
        user.PasswordSalt = salt;
    }

    public static async Task RecordSuccessfulLoginAsync(
        ApplicationDbContext dbContext,
        LoginUser user,
        string? ipAddress)
    {
        var utcNow = DateTime.UtcNow;

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
UPDATE AppLoginUsers
SET FailedLoginAttempts = 0,
    LockoutEndUtc = NULL,
    LastFailedLoginAt = NULL,
    LastLoginAt = @UtcNow,
    UpdatedAt = @UtcNow
WHERE Id = @Id;

INSERT INTO AuditLogs
(
    EntityName,
    EntityId,
    Action,
    NewValues,
    UserName,
    IpAddress
)
VALUES
(
    'Authentication',
    @EntityId,
    'Authentication Login Succeeded',
    'Authenticated session issued',
    @UserName,
    @IpAddress
);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@UtcNow", utcNow);
                HrmsDatabase.AddParameter(command, "@Id", user.Id);
                HrmsDatabase.AddParameter(
                    command,
                    "@EntityId",
                    user.Id.ToString());
                HrmsDatabase.AddParameter(
                    command,
                    "@UserName",
                    user.Username);
                HrmsDatabase.AddParameter(
                    command,
                    "@IpAddress",
                    NormalizeIpAddress(ipAddress));
            });
    }

    public static async Task RecordLogoutAsync(
        ApplicationDbContext dbContext,
        string? loginUserId,
        string? username,
        string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        await WriteAuditAsync(
            dbContext,
            loginUserId,
            "Authentication Logout",
            "Authenticated session ended",
            username,
            ipAddress);
    }

    private static async Task WriteAuditAsync(
        ApplicationDbContext dbContext,
        string? entityId,
        string action,
        string details,
        string? username,
        string? ipAddress)
    {
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
INSERT INTO AuditLogs
(
    EntityName,
    EntityId,
    Action,
    NewValues,
    UserName,
    IpAddress
)
VALUES
(
    'Authentication',
    @EntityId,
    @Action,
    @Details,
    @UserName,
    @IpAddress
);
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@EntityId",
                    entityId);
                HrmsDatabase.AddParameter(command, "@Action", action);
                HrmsDatabase.AddParameter(command, "@Details", details);
                HrmsDatabase.AddParameter(
                    command,
                    "@UserName",
                    username);
                HrmsDatabase.AddParameter(
                    command,
                    "@IpAddress",
                    NormalizeIpAddress(ipAddress));
            });
    }

    private static string? NormalizeIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        var value = ipAddress.Trim();
        return value.Length <= 80 ? value : value[..80];
    }

    private static string GetRequiredBootstrapPassword(
        string variableName)
    {
        var password = Environment.GetEnvironmentVariable(variableName);

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"The environment variable '{variableName}' must be set " +
                "before the first login account is created.");
        }

        return password;
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

        public int FailedLoginAttempts { get; set; }

        public DateTime? LockoutEndUtc { get; set; }

        public string EmployeeName { get; set; } = string.Empty;

        public bool IsLockedOut(DateTime utcNow)
        {
            return LockoutEndUtc.HasValue &&
                   LockoutEndUtc.Value > utcNow;
        }
    }
}
