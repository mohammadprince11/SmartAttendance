using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.UserAccess;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty]
    public UserInputModel Input { get; set; } = new();

    public List<UserRow> Users { get; set; } = new();

    public List<EmployeeOption> Employees { get; set; } = new();

    public string[] Roles { get; } =
    [
        "Admin",
        "HR Manager",
        "HR Officer",
        "Branch Manager",
        "Employee",
        "Finance Viewer"
    ];

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoginDatabase.EnsureCreatedAsync(_dbContext);
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoginDatabase.EnsureCreatedAsync(_dbContext);

        if (string.IsNullOrWhiteSpace(Input.Username) || string.IsNullOrWhiteSpace(Input.Role))
        {
            ErrorMessage = "اسم المستخدم والدور مطلوبان.";
            return RedirectToPage();
        }

        if (Input.Id == 0 && string.IsNullOrWhiteSpace(Input.Password))
        {
            ErrorMessage = "كلمة المرور مطلوبة عند إنشاء مستخدم جديد.";
            return RedirectToPage();
        }

        var duplicate = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT COUNT(*) FROM AppLoginUsers WHERE Id <> @Id AND Username = @Username",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", Input.Id);
                HrmsDatabase.AddParameter(command, "@Username", Input.Username.Trim());
            });

        if (duplicate > 0)
        {
            ErrorMessage = "اسم المستخدم موجود مسبقاً.";
            return RedirectToPage();
        }

        if (Input.Id > 0)
        {
            if (!string.IsNullOrWhiteSpace(Input.Password))
            {
                var salt = SimplePasswordHasher.CreateSalt();
                var hash = SimplePasswordHasher.HashPassword(Input.Password, salt);

                await HrmsDatabase.ExecuteAsync(
                    _dbContext,
                    """
UPDATE AppLoginUsers
SET EmployeeId = @EmployeeId,
    Username = @Username,
    PasswordHash = @PasswordHash,
    PasswordSalt = @PasswordSalt,
    Role = @Role,
    IsActive = @IsActive,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('AppLoginUsers', CAST(@Id AS nvarchar(80)), 'Update Login User With Password', @NewValues, 'Admin', @IpAddress);
""",
                    command =>
                    {
                        AddUserParameters(command, hash, salt);
                    });
            }
            else
            {
                await HrmsDatabase.ExecuteAsync(
                    _dbContext,
                    """
UPDATE AppLoginUsers
SET EmployeeId = @EmployeeId,
    Username = @Username,
    Role = @Role,
    IsActive = @IsActive,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('AppLoginUsers', CAST(@Id AS nvarchar(80)), 'Update Login User', @NewValues, 'Admin', @IpAddress);
""",
                    command =>
                    {
                        AddUserParameters(command, null, null);
                    });
            }

            SuccessMessage = "تم تعديل المستخدم.";
        }
        else
        {
            var salt = SimplePasswordHasher.CreateSalt();
            var hash = SimplePasswordHasher.HashPassword(Input.Password!, salt);

            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                """
INSERT INTO AppLoginUsers
(EmployeeId, Username, PasswordHash, PasswordSalt, Role, IsActive, CreatedAt)
VALUES
(@EmployeeId, @Username, @PasswordHash, @PasswordSalt, @Role, @IsActive, SYSUTCDATETIME());

DECLARE @NewId int = SCOPE_IDENTITY();

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('AppLoginUsers', CAST(@NewId AS nvarchar(80)), 'Create Login User', @NewValues, 'Admin', @IpAddress);
""",
                command =>
                {
                    AddUserParameters(command, hash, salt);
                });

            SuccessMessage = "تم إنشاء المستخدم.";
        }

        return RedirectToPage();

        void AddUserParameters(System.Data.Common.DbCommand command, string? hash, string? salt)
        {
            HrmsDatabase.AddParameter(command, "@Id", Input.Id);
            HrmsDatabase.AddParameter(command, "@EmployeeId", Input.EmployeeId == 0 ? null : Input.EmployeeId);
            HrmsDatabase.AddParameter(command, "@Username", Input.Username.Trim());
            HrmsDatabase.AddParameter(command, "@Role", Input.Role.Trim());
            HrmsDatabase.AddParameter(command, "@IsActive", Input.IsActive);
            HrmsDatabase.AddParameter(command, "@PasswordHash", hash);
            HrmsDatabase.AddParameter(command, "@PasswordSalt", salt);
            HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                ("EmployeeId", Input.EmployeeId),
                ("Username", Input.Username),
                ("Role", Input.Role),
                ("IsActive", Input.IsActive)));
            HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
        }
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        await LoginDatabase.EnsureCreatedAsync(_dbContext);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE AppLoginUsers
SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, UserName, IpAddress)
VALUES ('AppLoginUsers', CAST(@Id AS nvarchar(80)), 'Toggle Login User', 'Admin', @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        SuccessMessage = "تم تغيير حالة المستخدم.";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Users = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT
    u.Id,
    ISNULL(u.EmployeeId, 0) AS EmployeeId,
    u.Username,
    u.Role,
    u.IsActive,
    u.LastLoginAt,
    u.CreatedAt,
    ISNULL(e.EmployeeNo, '') AS EmployeeNo,
    ISNULL(e.FullName, '') AS EmployeeName
FROM AppLoginUsers u
LEFT JOIN Employees e ON u.EmployeeId = e.Id
WHERE
    (@Search IS NULL OR @Search = ''
     OR u.Username LIKE '%' + @Search + '%'
     OR u.Role LIKE '%' + @Search + '%'
     OR ISNULL(e.EmployeeNo, '') LIKE '%' + @Search + '%'
     OR ISNULL(e.FullName, '') LIKE '%' + @Search + '%')
ORDER BY u.IsActive DESC, u.Username;
""",
            command => HrmsDatabase.AddParameter(command, "@Search", Search),
            reader => new UserRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId") == 0 ? null : HrmsDatabase.GetInt(reader, "EmployeeId"),
                Username = HrmsDatabase.GetString(reader, "Username"),
                Role = HrmsDatabase.GetString(reader, "Role"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                LastLoginAt = HrmsDatabase.GetDateTime(reader, "LastLoginAt"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(reader, "EmployeeName")
            });

        Employees = await HrmsDatabase.QueryAsync(
            _dbContext,
            "SELECT TOP 2000 Id, EmployeeNo, FullName FROM Employees ORDER BY EmployeeNo",
            null,
            reader => new EmployeeOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Text = $"{HrmsDatabase.GetString(reader, "EmployeeNo")} - {HrmsDatabase.GetString(reader, "FullName")}"
            });
    }

    public class UserInputModel
    {
        public int Id { get; set; }

        public int? EmployeeId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string? Password { get; set; }

        public string Role { get; set; } = "Employee";

        public bool IsActive { get; set; } = true;
    }

    public class UserRow
    {
        public int Id { get; set; }

        public int? EmployeeId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string Role { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public DateTime? LastLoginAt { get; set; }

        public DateTime? CreatedAt { get; set; }

        public string EmployeeNo { get; set; } = string.Empty;

        public string EmployeeName { get; set; } = string.Empty;
    }

    public class EmployeeOption
    {
        public int Id { get; set; }

        public string Text { get; set; } = string.Empty;
    }
}
