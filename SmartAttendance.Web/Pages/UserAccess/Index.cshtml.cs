using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.UserAccess;

/// <summary>
/// إدارة مستخدمي النظام (/UserAccess): إنشاء حسابات دخول، ربطها بالموظفين،
/// الأدوار التوافقية، وحالة التفعيل. تعمل فوق جدول AppLoginUsers عبر LoginDatabase.
/// </summary>
public class IndexModel : PageModel
{
    private const string AdminRole = "Admin";

    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? EditLoginId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? EditSystemUserId { get; set; }

    [BindProperty]
    public IdentityInputModel Input { get; set; } = new();

    public List<IdentityRow> Identities { get; set; } = new();

    public List<EmployeeOption> Employees { get; set; } = new();

    public IReadOnlyList<RoleOption> Roles { get; } =
    [
        new("Admin", "مدير النظام", "Admin"),
        new("HR Manager", "مدير الموارد البشرية", "HR"),
        new("HR Officer", "مسؤول الموارد البشرية", "HR"),
        new("Branch Manager", "مدير فرع", "Viewer"),
        new("Finance Viewer", "عرض مالي", "Viewer"),
        new("Employee", "موظف", "Viewer")
    ];

    public string? PageError { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public int TotalCount => Identities.Count;

    public int LinkedCount => Identities.Count(x =>
        x.LinkStatus == IdentityLinkStatus.Linked);

    public int NeedsReviewCount => Identities.Count(x =>
        x.LinkStatus is IdentityLinkStatus.NeedsSync or
            IdentityLinkStatus.LoginOnly or
            IdentityLinkStatus.SystemOnly or
            IdentityLinkStatus.Conflict);

    public async Task<IActionResult> OnGetAsync()
    {
        await LoginDatabase.EnsureCreatedAsync(_dbContext);

        if (!IsAdministrator())
        {
            return Forbid();
        }

        await LoadAsync();

        if (EditLoginId.HasValue || EditSystemUserId.HasValue)
        {
            var loaded = await LoadEditorAsync(
                EditLoginId ?? 0,
                EditSystemUserId ?? 0);

            if (!loaded)
            {
                PageError = "تعذر العثور على الحساب المطلوب.";
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoginDatabase.EnsureCreatedAsync(_dbContext);

        if (!IsAdministrator())
        {
            return Forbid();
        }

        NormalizeInput();

        var validationError = await ValidateInputAsync();

        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return await FailAsync(validationError);
        }

        await EnsureSqlSetOptionsAsync();

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                HttpContext.RequestAborted);

        try
        {
            var existingLogin = Input.LoginId > 0
                ? await GetLoginAsync(Input.LoginId)
                : null;

            var existingSystemUser = Input.SystemUserId > 0
                ? await GetSystemUserAsync(Input.SystemUserId)
                : null;

            if (Input.LoginId > 0 && existingLogin == null)
            {
                await transaction.RollbackAsync();
                return await FailAsync("تعذر العثور على حساب الدخول المطلوب.");
            }

            if (Input.SystemUserId > 0 && existingSystemUser == null)
            {
                await transaction.RollbackAsync();
                return await FailAsync("تعذر العثور على هوية الصلاحيات المطلوبة.");
            }

            var currentLoginId = GetCurrentLoginId();

            if (existingLogin != null &&
                existingLogin.Id == currentLoginId &&
                !Input.IsActive)
            {
                await transaction.RollbackAsync();
                return await FailAsync(
                    "لا يمكن تعطيل حسابك الشخصي أثناء استخدامه.");
            }

            if (existingLogin != null &&
                existingLogin.Id == currentLoginId &&
                existingLogin.Role.Equals(
                    AdminRole,
                    StringComparison.OrdinalIgnoreCase) &&
                !Input.CompatibilityRole.Equals(
                    AdminRole,
                    StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync();
                return await FailAsync(
                    "لا يمكن إزالة دور المدير من حسابك الشخصي أثناء استخدامه.");
            }

            if (existingLogin != null &&
                existingLogin.IsActive &&
                existingLogin.Role.Equals(
                    AdminRole,
                    StringComparison.OrdinalIgnoreCase) &&
                (!Input.IsActive ||
                 !Input.CompatibilityRole.Equals(
                     AdminRole,
                     StringComparison.OrdinalIgnoreCase)))
            {
                var otherActiveAdmins =
                    await CountOtherActiveAdminsAsync(existingLogin.Id);

                if (otherActiveAdmins == 0)
                {
                    await transaction.RollbackAsync();
                    return await FailAsync(
                        "لا يمكن تعطيل أو تخفيض صلاحية آخر مدير فعال في النظام.");
                }
            }

            var duplicateUserName =
                await CountDuplicateUserNamesAsync(
                    Input.UserName,
                    Input.LoginId,
                    Input.SystemUserId);

            if (duplicateUserName > 0)
            {
                await transaction.RollbackAsync();
                return await FailAsync(
                    "اسم المستخدم مرتبط بحساب أو هوية أخرى.");
            }

            if (Input.EmployeeId.HasValue)
            {
                var duplicateEmployee =
                    await CountDuplicateEmployeeLinksAsync(
                        Input.EmployeeId.Value,
                        Input.LoginId,
                        Input.SystemUserId);

                if (duplicateEmployee > 0)
                {
                    await transaction.RollbackAsync();
                    return await FailAsync(
                        "الموظف المحدد مرتبط مسبقاً بحساب آخر.");
                }
            }

            var employee = Input.EmployeeId.HasValue
                ? await GetEmployeeAsync(Input.EmployeeId.Value)
                : null;

            if (Input.EmployeeId.HasValue && employee == null)
            {
                await transaction.RollbackAsync();
                return await FailAsync(
                    "تعذر العثور على الموظف المحدد.");
            }

            var displayName = employee?.FullName ??
                              Input.FullName.Trim();

            var actor = User.Identity?.Name ?? "System";
            var ipAddress =
                HttpContext.Connection.RemoteIpAddress?.ToString();
            var passwordChanged =
                !string.IsNullOrWhiteSpace(Input.Password);

            string? passwordSalt = null;
            string? passwordHash = null;

            if (passwordChanged)
            {
                passwordSalt = SimplePasswordHasher.CreateSalt();
                passwordHash = SimplePasswordHasher.HashPassword(
                    Input.Password!,
                    passwordSalt);
            }

            var loginId = existingLogin?.Id ?? 0;

            if (existingLogin == null)
            {
                loginId = await CreateLoginAsync(
                    displayName,
                    passwordHash!,
                    passwordSalt!,
                    actor);
            }
            else
            {
                await UpdateLoginAsync(
                    existingLogin.Id,
                    passwordHash,
                    passwordSalt);
            }

            var systemRole = MapSystemRole(Input.CompatibilityRole);
            var systemUserId = existingSystemUser?.Id ?? 0;

            if (existingSystemUser == null)
            {
                systemUserId = await CreateSystemUserAsync(
                    displayName,
                    systemRole,
                    actor);
            }
            else
            {
                await UpdateSystemUserAsync(
                    existingSystemUser.Id,
                    displayName,
                    systemRole,
                    actor);
            }

            await WriteAuditAsync(
                existingLogin == null && existingSystemUser == null
                    ? "Create Unified Identity"
                    : "Update Unified Identity",
                loginId,
                systemUserId,
                actor,
                ipAddress,
                passwordChanged);

            await transaction.CommitAsync(
                HttpContext.RequestAborted);

            SuccessMessage =
                existingLogin == null && existingSystemUser == null
                    ? "تم إنشاء حساب الدخول وهوية الصلاحيات وربطهما بنجاح."
                    : "تم تحديث الحساب وهوية الصلاحيات بنجاح.";

            return RedirectToPage(
                "./Index",
                new
                {
                    Search,
                    Status
                });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(
                HttpContext.RequestAborted);

            return await FailAsync(
                "تعذر حفظ الحساب بسبب تعارض في البيانات أو الربط.");
        }
    }

    public async Task<IActionResult> OnPostToggleAsync(
        int loginId,
        string? search,
        string? status)
    {
        await LoginDatabase.EnsureCreatedAsync(_dbContext);

        if (!IsAdministrator())
        {
            return Forbid();
        }

        await EnsureSqlSetOptionsAsync();

        var login = await GetLoginAsync(loginId);

        if (login == null)
        {
            SuccessMessage = null;
            TempData["ErrorMessage"] =
                "تعذر العثور على حساب الدخول.";
            return RedirectToPage(
                "./Index",
                new
                {
                    Search = search,
                    Status = status
                });
        }

        if (login.Id == GetCurrentLoginId())
        {
            TempData["ErrorMessage"] =
                "لا يمكن تغيير حالة حسابك الشخصي.";
            return RedirectToPage(
                "./Index",
                new
                {
                    Search = search,
                    Status = status
                });
        }

        if (login.IsActive &&
            login.Role.Equals(
                AdminRole,
                StringComparison.OrdinalIgnoreCase))
        {
            var otherActiveAdmins =
                await CountOtherActiveAdminsAsync(login.Id);

            if (otherActiveAdmins == 0)
            {
                TempData["ErrorMessage"] =
                    "لا يمكن تعطيل آخر مدير فعال في النظام.";
                return RedirectToPage(
                    "./Index",
                    new
                    {
                        Search = search,
                        Status = status
                    });
            }
        }

        var matches = await FindSystemMatchesAsync(
            login.EmployeeId,
            login.UserName);

        if (matches.Count > 1)
        {
            TempData["ErrorMessage"] =
                "يوجد تعارض في ربط الحساب. يجب إصلاح الربط قبل تغيير الحالة.";
            return RedirectToPage(
                "./Index",
                new
                {
                    Search = search,
                    Status = status
                });
        }

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(
                HttpContext.RequestAborted);

        try
        {
            var newState = !login.IsActive;
            var actor = User.Identity?.Name ?? "System";
            var ipAddress =
                HttpContext.Connection.RemoteIpAddress?.ToString();

            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                """
UPDATE AppLoginUsers
SET IsActive = @IsActive,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @LoginId;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(
                        command,
                        "@IsActive",
                        newState);
                    HrmsDatabase.AddParameter(
                        command,
                        "@LoginId",
                        login.Id);
                });

            if (matches.Count == 1)
            {
                await HrmsDatabase.ExecuteAsync(
                    _dbContext,
                    """
UPDATE SystemUsers
SET IsActive = @IsActive,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @Actor
WHERE Id = @SystemUserId;
""",
                    command =>
                    {
                        HrmsDatabase.AddParameter(
                            command,
                            "@IsActive",
                            newState);
                        HrmsDatabase.AddParameter(
                            command,
                            "@Actor",
                            actor);
                        HrmsDatabase.AddParameter(
                            command,
                            "@SystemUserId",
                            matches[0]);
                    });
            }

            await WriteAuditAsync(
                newState
                    ? "Activate Unified Identity"
                    : "Deactivate Unified Identity",
                login.Id,
                matches.Count == 1 ? matches[0] : 0,
                actor,
                ipAddress,
                false);

            await transaction.CommitAsync(
                HttpContext.RequestAborted);

            SuccessMessage = newState
                ? "تم تفعيل الحساب."
                : "تم تعطيل الحساب.";
        }
        catch
        {
            await transaction.RollbackAsync(
                HttpContext.RequestAborted);

            TempData["ErrorMessage"] =
                "تعذر تغيير حالة الحساب.";
        }

        return RedirectToPage(
            "./Index",
            new
            {
                Search = search,
                Status = status
            });
    }

    private async Task LoadAsync()
    {
        Employees = await LoadEmployeesAsync();
        Identities = await LoadIdentityRowsAsync();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var normalizedSearch = Search.Trim();

            Identities = Identities
                .Where(x =>
                    x.UserName.Contains(
                        normalizedSearch,
                        StringComparison.OrdinalIgnoreCase) ||
                    x.DisplayName.Contains(
                        normalizedSearch,
                        StringComparison.OrdinalIgnoreCase) ||
                    x.EmployeeNo.Contains(
                        normalizedSearch,
                        StringComparison.OrdinalIgnoreCase) ||
                    x.EmployeeName.Contains(
                        normalizedSearch,
                        StringComparison.OrdinalIgnoreCase) ||
                    x.CompatibilityRole.Contains(
                        normalizedSearch,
                        StringComparison.OrdinalIgnoreCase) ||
                    x.SystemRoleLabel.Contains(
                        normalizedSearch,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(Status) &&
            Enum.TryParse<IdentityLinkStatus>(
                Status,
                true,
                out var parsedStatus))
        {
            Identities = Identities
                .Where(x => x.LinkStatus == parsedStatus)
                .ToList();
        }
    }

    private async Task<bool> LoadEditorAsync(
        int loginId,
        int systemUserId)
    {
        var login = loginId > 0
            ? await GetLoginAsync(loginId)
            : null;

        var systemUser = systemUserId > 0
            ? await GetSystemUserAsync(systemUserId)
            : null;

        if (login == null && systemUser == null)
        {
            return false;
        }

        if (login != null && systemUser == null)
        {
            var matches = await FindSystemMatchesAsync(
                login.EmployeeId,
                login.UserName);

            if (matches.Count == 1)
            {
                systemUser = await GetSystemUserAsync(matches[0]);
            }
        }

        if (systemUser != null && login == null)
        {
            login = await FindLoginMatchAsync(
                systemUser.EmployeeId,
                systemUser.UserName);
        }

        Input = new IdentityInputModel
        {
            LoginId = login?.Id ?? 0,
            SystemUserId = systemUser?.Id ?? 0,
            EmployeeId =
                login?.EmployeeId ??
                systemUser?.EmployeeId,
            FullName =
                systemUser?.FullName ??
                login?.EmployeeName ??
                login?.UserName ??
                string.Empty,
            UserName =
                login?.UserName ??
                systemUser?.UserName ??
                string.Empty,
            Email = systemUser?.Email,
            CompatibilityRole =
                login?.Role ??
                MapCompatibilityRole(systemUser?.Role ?? 3),
            IsActive =
                login?.IsActive ??
                systemUser?.IsActive ??
                true,
            Notes = systemUser?.Notes
        };

        return true;
    }

    private async Task<string?> ValidateInputAsync()
    {
        if (string.IsNullOrWhiteSpace(Input.UserName))
        {
            return "اسم المستخدم مطلوب.";
        }

        if (Input.UserName.Length > 100)
        {
            return "اسم المستخدم يجب ألا يتجاوز 100 حرف.";
        }

        if (!Roles.Any(x =>
                x.Value.Equals(
                    Input.CompatibilityRole,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return "دور التوافق المحدد غير صالح.";
        }

        if (Input.CompatibilityRole.Equals(
                "Employee",
                StringComparison.OrdinalIgnoreCase) &&
            !Input.EmployeeId.HasValue)
        {
            return "حساب الموظف يجب أن يكون مرتبطاً بموظف.";
        }

        if (!Input.EmployeeId.HasValue &&
            string.IsNullOrWhiteSpace(Input.FullName))
        {
            return "الاسم الكامل مطلوب للحساب غير المرتبط بموظف.";
        }

        if (!string.IsNullOrWhiteSpace(Input.Email) &&
            !new EmailAddressAttribute().IsValid(Input.Email))
        {
            return "صيغة البريد الإلكتروني غير صحيحة.";
        }

        var creatingLogin = Input.LoginId <= 0;

        if (creatingLogin &&
            string.IsNullOrWhiteSpace(Input.Password))
        {
            return "كلمة المرور مطلوبة عند إنشاء حساب الدخول.";
        }

        if (!string.IsNullOrWhiteSpace(Input.Password))
        {
            if (Input.Password.Length < 8)
            {
                return "كلمة المرور يجب ألا تقل عن 8 أحرف.";
            }

            if (!string.Equals(
                    Input.Password,
                    Input.ConfirmPassword,
                    StringComparison.Ordinal))
            {
                return "كلمة المرور وتأكيدها غير متطابقين.";
            }
        }

        return null;
    }

    private void NormalizeInput()
    {
        Input.UserName = Input.UserName?.Trim() ?? string.Empty;
        Input.FullName = Input.FullName?.Trim() ?? string.Empty;
        Input.Email = string.IsNullOrWhiteSpace(Input.Email)
            ? null
            : Input.Email.Trim();
        Input.CompatibilityRole =
            Input.CompatibilityRole?.Trim() ?? string.Empty;
        Input.Notes = string.IsNullOrWhiteSpace(Input.Notes)
            ? null
            : Input.Notes.Trim();

        if (Input.EmployeeId <= 0)
        {
            Input.EmployeeId = null;
        }
    }

    private async Task<IActionResult> FailAsync(string message)
    {
        PageError = message;
        Input.Password = null;
        Input.ConfirmPassword = null;
        await LoadAsync();
        return Page();
    }

    private bool IsAdministrator()
    {
        return User.IsInRole(AdminRole);
    }

    private int GetCurrentLoginId()
    {
        var value = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        return int.TryParse(value, out var id)
            ? id
            : 0;
    }

    private async Task EnsureSqlSetOptionsAsync()
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
""");
    }

    private async Task<int> CreateLoginAsync(
        string displayName,
        string passwordHash,
        string passwordSalt,
        string actor)
    {
        return await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
INSERT INTO AppLoginUsers
(
    EmployeeId,
    Username,
    PasswordHash,
    PasswordSalt,
    Role,
    IsActive,
    CreatedAt
)
VALUES
(
    @EmployeeId,
    @Username,
    @PasswordHash,
    @PasswordSalt,
    @Role,
    @IsActive,
    SYSUTCDATETIME()
);

SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@EmployeeId",
                    Input.EmployeeId);
                HrmsDatabase.AddParameter(
                    command,
                    "@Username",
                    Input.UserName);
                HrmsDatabase.AddParameter(
                    command,
                    "@PasswordHash",
                    passwordHash);
                HrmsDatabase.AddParameter(
                    command,
                    "@PasswordSalt",
                    passwordSalt);
                HrmsDatabase.AddParameter(
                    command,
                    "@Role",
                    Input.CompatibilityRole);
                HrmsDatabase.AddParameter(
                    command,
                    "@IsActive",
                    Input.IsActive);
            });
    }

    private async Task UpdateLoginAsync(
        int loginId,
        string? passwordHash,
        string? passwordSalt)
    {
        if (!string.IsNullOrWhiteSpace(passwordHash) &&
            !string.IsNullOrWhiteSpace(passwordSalt))
        {
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
WHERE Id = @LoginId;
""",
                command =>
                {
                    AddLoginUpdateParameters(
                        command,
                        loginId,
                        passwordHash,
                        passwordSalt);
                });

            return;
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE AppLoginUsers
SET EmployeeId = @EmployeeId,
    Username = @Username,
    Role = @Role,
    IsActive = @IsActive,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @LoginId;
""",
            command =>
            {
                AddLoginUpdateParameters(
                    command,
                    loginId,
                    null,
                    null);
            });
    }

    private void AddLoginUpdateParameters(
        System.Data.Common.DbCommand command,
        int loginId,
        string? passwordHash,
        string? passwordSalt)
    {
        HrmsDatabase.AddParameter(
            command,
            "@LoginId",
            loginId);
        HrmsDatabase.AddParameter(
            command,
            "@EmployeeId",
            Input.EmployeeId);
        HrmsDatabase.AddParameter(
            command,
            "@Username",
            Input.UserName);
        HrmsDatabase.AddParameter(
            command,
            "@Role",
            Input.CompatibilityRole);
        HrmsDatabase.AddParameter(
            command,
            "@IsActive",
            Input.IsActive);
        HrmsDatabase.AddParameter(
            command,
            "@PasswordHash",
            passwordHash);
        HrmsDatabase.AddParameter(
            command,
            "@PasswordSalt",
            passwordSalt);
    }

    private async Task<int> CreateSystemUserAsync(
        string displayName,
        int systemRole,
        string actor)
    {
        return await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
INSERT INTO SystemUsers
(
    FullName,
    UserName,
    Email,
    Role,
    IsActive,
    Notes,
    EmployeeId,
    CreatedAt,
    IsDeleted,
    CreatedBy
)
VALUES
(
    @FullName,
    @UserName,
    @Email,
    @Role,
    @IsActive,
    @Notes,
    @EmployeeId,
    SYSUTCDATETIME(),
    0,
    @Actor
);

SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                AddSystemUserParameters(
                    command,
                    displayName,
                    systemRole,
                    actor);
            });
    }

    private async Task UpdateSystemUserAsync(
        int systemUserId,
        string displayName,
        int systemRole,
        string actor)
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE SystemUsers
SET FullName = @FullName,
    UserName = @UserName,
    Email = @Email,
    Role = @Role,
    IsActive = @IsActive,
    Notes = @Notes,
    EmployeeId = @EmployeeId,
    UpdatedAt = SYSUTCDATETIME(),
    UpdatedBy = @Actor,
    IsDeleted = 0
WHERE Id = @SystemUserId;
""",
            command =>
            {
                AddSystemUserParameters(
                    command,
                    displayName,
                    systemRole,
                    actor);
                HrmsDatabase.AddParameter(
                    command,
                    "@SystemUserId",
                    systemUserId);
            });
    }

    private void AddSystemUserParameters(
        System.Data.Common.DbCommand command,
        string displayName,
        int systemRole,
        string actor)
    {
        HrmsDatabase.AddParameter(
            command,
            "@FullName",
            displayName);
        HrmsDatabase.AddParameter(
            command,
            "@UserName",
            Input.UserName);
        HrmsDatabase.AddParameter(
            command,
            "@Email",
            Input.Email);
        HrmsDatabase.AddParameter(
            command,
            "@Role",
            systemRole);
        HrmsDatabase.AddParameter(
            command,
            "@IsActive",
            Input.IsActive);
        HrmsDatabase.AddParameter(
            command,
            "@Notes",
            Input.Notes);
        HrmsDatabase.AddParameter(
            command,
            "@EmployeeId",
            Input.EmployeeId);
        HrmsDatabase.AddParameter(
            command,
            "@Actor",
            actor);
    }

    private async Task WriteAuditAsync(
        string action,
        int loginId,
        int systemUserId,
        string actor,
        string? ipAddress,
        bool passwordChanged)
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
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
    'UnifiedIdentity',
    @EntityId,
    @Action,
    @NewValues,
    @Actor,
    @IpAddress
);
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@EntityId",
                    $"{loginId}:{systemUserId}");
                HrmsDatabase.AddParameter(
                    command,
                    "@Action",
                    action);
                HrmsDatabase.AddParameter(
                    command,
                    "@NewValues",
                    HrmsDatabase.JsonLine(
                        ("LoginId", loginId),
                        ("SystemUserId", systemUserId),
                        ("EmployeeId", Input.EmployeeId),
                        ("UserName", Input.UserName),
                        ("CompatibilityRole", Input.CompatibilityRole),
                        ("SystemRole", MapSystemRoleLabel(
                            MapSystemRole(Input.CompatibilityRole))),
                        ("IsActive", Input.IsActive),
                        ("PasswordChanged", passwordChanged)));
                HrmsDatabase.AddParameter(
                    command,
                    "@Actor",
                    actor);
                HrmsDatabase.AddParameter(
                    command,
                    "@IpAddress",
                    ipAddress);
            });
    }

    private async Task<List<IdentityRow>> LoadIdentityRowsAsync()
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT
    u.Id AS LoginId,
    ISNULL(u.EmployeeId, 0) AS LoginEmployeeId,
    u.Username AS LoginUserName,
    u.Role AS CompatibilityRole,
    u.IsActive AS LoginIsActive,
    u.LastLoginAt,
    u.CreatedAt AS LoginCreatedAt,
    ISNULL(e.EmployeeNo, '') AS EmployeeNo,
    ISNULL(e.FullName, '') AS EmployeeName,
    ISNULL(su.Id, 0) AS SystemUserId,
    ISNULL(su.EmployeeId, 0) AS SystemEmployeeId,
    ISNULL(su.FullName, '') AS SystemFullName,
    ISNULL(su.UserName, '') AS SystemUserName,
    ISNULL(su.Email, '') AS SystemEmail,
    ISNULL(su.Role, 0) AS SystemRole,
    ISNULL(su.IsActive, 0) AS SystemIsActive,
    ISNULL(su.Notes, '') AS SystemNotes,
    ISNULL(su.PermissionCount, 0) AS PermissionCount,
    (
        SELECT COUNT(*)
        FROM SystemUsers sx
        WHERE sx.IsDeleted = 0
          AND
          (
              (u.EmployeeId IS NOT NULL AND sx.EmployeeId = u.EmployeeId)
              OR sx.UserName = u.Username
          )
    ) AS MatchCount
FROM AppLoginUsers u
LEFT JOIN Employees e
    ON e.Id = u.EmployeeId
OUTER APPLY
(
    SELECT TOP 1
        sx.Id,
        sx.EmployeeId,
        sx.FullName,
        sx.UserName,
        sx.Email,
        sx.Role,
        sx.IsActive,
        sx.Notes,
        (
            SELECT COUNT(*)
            FROM SystemUserPermissions sup
            WHERE sup.SystemUserId = sx.Id
              AND sup.IsDeleted = 0
        ) AS PermissionCount
    FROM SystemUsers sx
    WHERE sx.IsDeleted = 0
      AND
      (
          (u.EmployeeId IS NOT NULL AND sx.EmployeeId = u.EmployeeId)
          OR sx.UserName = u.Username
      )
    ORDER BY
        CASE
            WHEN u.EmployeeId IS NOT NULL
             AND sx.EmployeeId = u.EmployeeId THEN 0
            ELSE 1
        END,
        sx.Id
) su
ORDER BY u.IsActive DESC, u.Username;
""",
            null,
            reader => new IdentityRow
            {
                LoginId = HrmsDatabase.GetInt(
                    reader,
                    "LoginId"),
                SystemUserId = HrmsDatabase.GetInt(
                    reader,
                    "SystemUserId"),
                EmployeeId =
                    HrmsDatabase.GetInt(
                        reader,
                        "LoginEmployeeId") is var loginEmployeeId &&
                    loginEmployeeId > 0
                        ? loginEmployeeId
                        : HrmsDatabase.GetInt(
                            reader,
                            "SystemEmployeeId") is var systemEmployeeId &&
                          systemEmployeeId > 0
                            ? systemEmployeeId
                            : null,
                UserName = HrmsDatabase.GetString(
                    reader,
                    "LoginUserName"),
                DisplayName =
                    string.IsNullOrWhiteSpace(
                        HrmsDatabase.GetString(
                            reader,
                            "SystemFullName"))
                        ? HrmsDatabase.GetString(
                            reader,
                            "EmployeeName")
                        : HrmsDatabase.GetString(
                            reader,
                            "SystemFullName"),
                EmployeeNo = HrmsDatabase.GetString(
                    reader,
                    "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(
                    reader,
                    "EmployeeName"),
                Email = HrmsDatabase.GetString(
                    reader,
                    "SystemEmail"),
                CompatibilityRole = HrmsDatabase.GetString(
                    reader,
                    "CompatibilityRole"),
                SystemRole = HrmsDatabase.GetInt(
                    reader,
                    "SystemRole"),
                IsActive = HrmsDatabase.GetBool(
                    reader,
                    "LoginIsActive"),
                SystemIsActive = HrmsDatabase.GetBool(
                    reader,
                    "SystemIsActive"),
                LastLoginAt = HrmsDatabase.GetDateTime(
                    reader,
                    "LastLoginAt"),
                CreatedAt = HrmsDatabase.GetDateTime(
                    reader,
                    "LoginCreatedAt"),
                PermissionCount = HrmsDatabase.GetInt(
                    reader,
                    "PermissionCount"),
                MatchCount = HrmsDatabase.GetInt(
                    reader,
                    "MatchCount"),
                SystemUserName = HrmsDatabase.GetString(
                    reader,
                    "SystemUserName")
            });

        foreach (var row in rows)
        {
            row.LinkStatus = ResolveLoginRowStatus(row);
        }

        var systemOnlyRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT
    su.Id AS SystemUserId,
    ISNULL(su.EmployeeId, 0) AS EmployeeId,
    su.FullName,
    su.UserName,
    ISNULL(su.Email, '') AS Email,
    su.Role,
    su.IsActive,
    su.CreatedAt,
    ISNULL(su.Notes, '') AS Notes,
    ISNULL(e.EmployeeNo, '') AS EmployeeNo,
    ISNULL(e.FullName, '') AS EmployeeName,
    (
        SELECT COUNT(*)
        FROM SystemUserPermissions sup
        WHERE sup.SystemUserId = su.Id
          AND sup.IsDeleted = 0
    ) AS PermissionCount
FROM SystemUsers su
LEFT JOIN Employees e
    ON e.Id = su.EmployeeId
WHERE su.IsDeleted = 0
  AND NOT EXISTS
  (
      SELECT 1
      FROM AppLoginUsers u
      WHERE
          (su.EmployeeId IS NOT NULL AND u.EmployeeId = su.EmployeeId)
          OR u.Username = su.UserName
  )
ORDER BY su.IsActive DESC, su.FullName;
""",
            null,
            reader => new IdentityRow
            {
                LoginId = 0,
                SystemUserId = HrmsDatabase.GetInt(
                    reader,
                    "SystemUserId"),
                EmployeeId =
                    HrmsDatabase.GetInt(
                        reader,
                        "EmployeeId") is var employeeId &&
                    employeeId > 0
                        ? employeeId
                        : null,
                UserName = HrmsDatabase.GetString(
                    reader,
                    "UserName"),
                SystemUserName = HrmsDatabase.GetString(
                    reader,
                    "UserName"),
                DisplayName = HrmsDatabase.GetString(
                    reader,
                    "FullName"),
                EmployeeNo = HrmsDatabase.GetString(
                    reader,
                    "EmployeeNo"),
                EmployeeName = HrmsDatabase.GetString(
                    reader,
                    "EmployeeName"),
                Email = HrmsDatabase.GetString(
                    reader,
                    "Email"),
                CompatibilityRole = MapCompatibilityRole(
                    HrmsDatabase.GetInt(
                        reader,
                        "Role")),
                SystemRole = HrmsDatabase.GetInt(
                    reader,
                    "Role"),
                IsActive = HrmsDatabase.GetBool(
                    reader,
                    "IsActive"),
                SystemIsActive = HrmsDatabase.GetBool(
                    reader,
                    "IsActive"),
                CreatedAt = HrmsDatabase.GetDateTime(
                    reader,
                    "CreatedAt"),
                PermissionCount = HrmsDatabase.GetInt(
                    reader,
                    "PermissionCount"),
                LinkStatus = IdentityLinkStatus.SystemOnly
            });

        rows.AddRange(systemOnlyRows);

        return rows
            .OrderBy(x => StatusOrder(x.LinkStatus))
            .ThenByDescending(x => x.IsActive)
            .ThenBy(x => x.DisplayName)
            .ThenBy(x => x.UserName)
            .ToList();
    }

    private static IdentityLinkStatus ResolveLoginRowStatus(
        IdentityRow row)
    {
        if (row.MatchCount > 1)
        {
            return IdentityLinkStatus.Conflict;
        }

        if (row.SystemUserId <= 0)
        {
            return IdentityLinkStatus.LoginOnly;
        }

        var expectedSystemRole =
            MapSystemRole(row.CompatibilityRole);

        var sameUserName = string.Equals(
            row.UserName,
            row.SystemUserName,
            StringComparison.OrdinalIgnoreCase);

        var sameActiveState =
            row.IsActive == row.SystemIsActive;

        var sameRole =
            row.SystemRole == expectedSystemRole;

        return sameUserName &&
               sameActiveState &&
               sameRole
            ? IdentityLinkStatus.Linked
            : IdentityLinkStatus.NeedsSync;
    }

    private async Task<List<EmployeeOption>> LoadEmployeesAsync()
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT
    e.Id,
    e.EmployeeNo,
    e.FullName,
    e.IsActive,
    CASE
        WHEN EXISTS
        (
            SELECT 1
            FROM AppLoginUsers u
            WHERE u.EmployeeId = e.Id
        )
        OR EXISTS
        (
            SELECT 1
            FROM SystemUsers su
            WHERE su.EmployeeId = e.Id
              AND su.IsDeleted = 0
        )
        THEN 1
        ELSE 0
    END AS IsLinked
FROM Employees e
WHERE e.IsDeleted = 0
ORDER BY e.IsActive DESC, e.EmployeeNo, e.FullName;
""",
            null,
            reader => new EmployeeOption
            {
                Id = HrmsDatabase.GetInt(
                    reader,
                    "Id"),
                EmployeeNo = HrmsDatabase.GetString(
                    reader,
                    "EmployeeNo"),
                FullName = HrmsDatabase.GetString(
                    reader,
                    "FullName"),
                IsActive = HrmsDatabase.GetBool(
                    reader,
                    "IsActive"),
                IsLinked = HrmsDatabase.GetBool(
                    reader,
                    "IsLinked")
            });
    }

    private async Task<LoginRecord?> GetLoginAsync(int id)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    u.Id,
    ISNULL(u.EmployeeId, 0) AS EmployeeId,
    u.Username,
    u.Role,
    u.IsActive,
    ISNULL(e.FullName, '') AS EmployeeName
FROM AppLoginUsers u
LEFT JOIN Employees e ON e.Id = u.EmployeeId
WHERE u.Id = @Id;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@Id",
                id),
            reader => new LoginRecord
            {
                Id = HrmsDatabase.GetInt(
                    reader,
                    "Id"),
                EmployeeId =
                    HrmsDatabase.GetInt(
                        reader,
                        "EmployeeId") is var employeeId &&
                    employeeId > 0
                        ? employeeId
                        : null,
                UserName = HrmsDatabase.GetString(
                    reader,
                    "Username"),
                Role = HrmsDatabase.GetString(
                    reader,
                    "Role"),
                IsActive = HrmsDatabase.GetBool(
                    reader,
                    "IsActive"),
                EmployeeName = HrmsDatabase.GetString(
                    reader,
                    "EmployeeName")
            });

        return rows.FirstOrDefault();
    }

    private async Task<SystemUserRecord?> GetSystemUserAsync(
        int id)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    Id,
    ISNULL(EmployeeId, 0) AS EmployeeId,
    FullName,
    UserName,
    ISNULL(Email, '') AS Email,
    Role,
    IsActive,
    ISNULL(Notes, '') AS Notes
FROM SystemUsers
WHERE Id = @Id
  AND IsDeleted = 0;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@Id",
                id),
            reader => new SystemUserRecord
            {
                Id = HrmsDatabase.GetInt(
                    reader,
                    "Id"),
                EmployeeId =
                    HrmsDatabase.GetInt(
                        reader,
                        "EmployeeId") is var employeeId &&
                    employeeId > 0
                        ? employeeId
                        : null,
                FullName = HrmsDatabase.GetString(
                    reader,
                    "FullName"),
                UserName = HrmsDatabase.GetString(
                    reader,
                    "UserName"),
                Email = HrmsDatabase.GetString(
                    reader,
                    "Email"),
                Role = HrmsDatabase.GetInt(
                    reader,
                    "Role"),
                IsActive = HrmsDatabase.GetBool(
                    reader,
                    "IsActive"),
                Notes = HrmsDatabase.GetString(
                    reader,
                    "Notes")
            });

        return rows.FirstOrDefault();
    }

    private async Task<EmployeeRecord?> GetEmployeeAsync(
        int id)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    Id,
    EmployeeNo,
    FullName
FROM Employees
WHERE Id = @Id
  AND IsDeleted = 0;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@Id",
                id),
            reader => new EmployeeRecord
            {
                Id = HrmsDatabase.GetInt(
                    reader,
                    "Id"),
                EmployeeNo = HrmsDatabase.GetString(
                    reader,
                    "EmployeeNo"),
                FullName = HrmsDatabase.GetString(
                    reader,
                    "FullName")
            });

        return rows.FirstOrDefault();
    }

    private async Task<LoginRecord?> FindLoginMatchAsync(
        int? employeeId,
        string userName)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 2
    u.Id,
    ISNULL(u.EmployeeId, 0) AS EmployeeId,
    u.Username,
    u.Role,
    u.IsActive,
    ISNULL(e.FullName, '') AS EmployeeName
FROM AppLoginUsers u
LEFT JOIN Employees e ON e.Id = u.EmployeeId
WHERE
    (@EmployeeId IS NOT NULL AND u.EmployeeId = @EmployeeId)
    OR u.Username = @UserName
ORDER BY
    CASE
        WHEN @EmployeeId IS NOT NULL
         AND u.EmployeeId = @EmployeeId THEN 0
        ELSE 1
    END,
    u.Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@EmployeeId",
                    employeeId);
                HrmsDatabase.AddParameter(
                    command,
                    "@UserName",
                    userName);
            },
            reader => new LoginRecord
            {
                Id = HrmsDatabase.GetInt(
                    reader,
                    "Id"),
                EmployeeId =
                    HrmsDatabase.GetInt(
                        reader,
                        "EmployeeId") is var foundEmployeeId &&
                    foundEmployeeId > 0
                        ? foundEmployeeId
                        : null,
                UserName = HrmsDatabase.GetString(
                    reader,
                    "Username"),
                Role = HrmsDatabase.GetString(
                    reader,
                    "Role"),
                IsActive = HrmsDatabase.GetBool(
                    reader,
                    "IsActive"),
                EmployeeName = HrmsDatabase.GetString(
                    reader,
                    "EmployeeName")
            });

        return rows.Count == 1
            ? rows[0]
            : null;
    }

    private async Task<List<int>> FindSystemMatchesAsync(
        int? employeeId,
        string userName)
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT Id
FROM SystemUsers
WHERE IsDeleted = 0
  AND
  (
      (@EmployeeId IS NOT NULL AND EmployeeId = @EmployeeId)
      OR UserName = @UserName
  )
ORDER BY Id;
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@EmployeeId",
                    employeeId);
                HrmsDatabase.AddParameter(
                    command,
                    "@UserName",
                    userName);
            },
            reader => HrmsDatabase.GetInt(
                reader,
                "Id"));
    }

    private async Task<int> CountDuplicateUserNamesAsync(
        string userName,
        int loginId,
        int systemUserId)
    {
        return await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
SELECT
    (
        SELECT COUNT(*)
        FROM AppLoginUsers WITH (UPDLOCK, HOLDLOCK)
        WHERE Id <> @LoginId
          AND Username = @UserName
    )
    +
    (
        SELECT COUNT(*)
        FROM SystemUsers WITH (UPDLOCK, HOLDLOCK)
        WHERE Id <> @SystemUserId
          AND UserName = @UserName
    );
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@LoginId",
                    loginId);
                HrmsDatabase.AddParameter(
                    command,
                    "@SystemUserId",
                    systemUserId);
                HrmsDatabase.AddParameter(
                    command,
                    "@UserName",
                    userName);
            });
    }

    private async Task<int> CountDuplicateEmployeeLinksAsync(
        int employeeId,
        int loginId,
        int systemUserId)
    {
        return await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
SELECT
    (
        SELECT COUNT(*)
        FROM AppLoginUsers WITH (UPDLOCK, HOLDLOCK)
        WHERE Id <> @LoginId
          AND EmployeeId = @EmployeeId
    )
    +
    (
        SELECT COUNT(*)
        FROM SystemUsers WITH (UPDLOCK, HOLDLOCK)
        WHERE Id <> @SystemUserId
          AND EmployeeId = @EmployeeId
    );
""",
            command =>
            {
                HrmsDatabase.AddParameter(
                    command,
                    "@LoginId",
                    loginId);
                HrmsDatabase.AddParameter(
                    command,
                    "@SystemUserId",
                    systemUserId);
                HrmsDatabase.AddParameter(
                    command,
                    "@EmployeeId",
                    employeeId);
            });
    }

    private async Task<int> CountOtherActiveAdminsAsync(
        int excludedLoginId)
    {
        return await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
SELECT COUNT(*)
FROM AppLoginUsers WITH (UPDLOCK, HOLDLOCK)
WHERE Id <> @ExcludedLoginId
  AND Role = 'Admin'
  AND IsActive = 1;
""",
            command => HrmsDatabase.AddParameter(
                command,
                "@ExcludedLoginId",
                excludedLoginId));
    }

    private static int MapSystemRole(string compatibilityRole)
    {
        if (compatibilityRole.Equals(
                "Admin",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (compatibilityRole.Equals(
                "HR Manager",
                StringComparison.OrdinalIgnoreCase) ||
            compatibilityRole.Equals(
                "HR Officer",
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    public static string MapSystemRoleLabel(int role)
    {
        return role switch
        {
            1 => "Admin",
            2 => "HR",
            _ => "Viewer"
        };
    }

    private static string MapCompatibilityRole(int systemRole)
    {
        return systemRole switch
        {
            1 => "Admin",
            2 => "HR Officer",
            _ => "Employee"
        };
    }

    private static int StatusOrder(
        IdentityLinkStatus status)
    {
        return status switch
        {
            IdentityLinkStatus.Conflict => 0,
            IdentityLinkStatus.LoginOnly => 1,
            IdentityLinkStatus.SystemOnly => 2,
            IdentityLinkStatus.NeedsSync => 3,
            _ => 4
        };
    }

    public class IdentityInputModel
    {
        public int LoginId { get; set; }

        public int SystemUserId { get; set; }

        public int? EmployeeId { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string? Email { get; set; }

        public string CompatibilityRole { get; set; } =
            "Employee";

        public bool IsActive { get; set; } = true;

        public string? Password { get; set; }

        public string? ConfirmPassword { get; set; }

        public string? Notes { get; set; }

        public bool IsEditing =>
            LoginId > 0 || SystemUserId > 0;
    }

    public class IdentityRow
    {
        public int LoginId { get; set; }

        public int SystemUserId { get; set; }

        public int? EmployeeId { get; set; }

        public string UserName { get; set; } = string.Empty;

        public string SystemUserName { get; set; } =
            string.Empty;

        public string DisplayName { get; set; } =
            string.Empty;

        public string EmployeeNo { get; set; } =
            string.Empty;

        public string EmployeeName { get; set; } =
            string.Empty;

        public string Email { get; set; } =
            string.Empty;

        public string CompatibilityRole { get; set; } =
            string.Empty;

        public int SystemRole { get; set; }

        public bool IsActive { get; set; }

        public bool SystemIsActive { get; set; }

        public DateTime? LastLoginAt { get; set; }

        public DateTime? CreatedAt { get; set; }

        public int PermissionCount { get; set; }

        public int MatchCount { get; set; }

        public IdentityLinkStatus LinkStatus { get; set; }

        public string SystemRoleLabel =>
            MapSystemRoleLabel(SystemRole);

        public string StatusLabel =>
            LinkStatus switch
            {
                IdentityLinkStatus.Linked =>
                    "مرتبط بشكل صحيح",
                IdentityLinkStatus.NeedsSync =>
                    "يحتاج مزامنة",
                IdentityLinkStatus.LoginOnly =>
                    "حساب دخول فقط",
                IdentityLinkStatus.SystemOnly =>
                    "هوية صلاحيات فقط",
                IdentityLinkStatus.Conflict =>
                    "تعارض في الربط",
                _ => "غير معروف"
            };

        public string StatusCss =>
            LinkStatus switch
            {
                IdentityLinkStatus.Linked => "linked",
                IdentityLinkStatus.NeedsSync => "sync",
                IdentityLinkStatus.LoginOnly => "login-only",
                IdentityLinkStatus.SystemOnly => "system-only",
                IdentityLinkStatus.Conflict => "conflict",
                _ => "unknown"
            };

        public bool CanEdit =>
            LinkStatus != IdentityLinkStatus.Conflict;

        public bool CanToggle =>
            LoginId > 0 &&
            LinkStatus != IdentityLinkStatus.Conflict;
    }

    public enum IdentityLinkStatus
    {
        Linked,
        NeedsSync,
        LoginOnly,
        SystemOnly,
        Conflict
    }

    public record RoleOption(
        string Value,
        string Label,
        string SystemTemplate);

    public class EmployeeOption
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } =
            string.Empty;

        public string FullName { get; set; } =
            string.Empty;

        public bool IsActive { get; set; }

        public bool IsLinked { get; set; }

        public string DisplayText =>
            $"{EmployeeNo} - {FullName}" +
            (IsActive ? string.Empty : " - غير فعال") +
            (IsLinked ? " - مرتبط" : string.Empty);
    }

    private class LoginRecord
    {
        public int Id { get; set; }

        public int? EmployeeId { get; set; }

        public string UserName { get; set; } =
            string.Empty;

        public string Role { get; set; } =
            string.Empty;

        public bool IsActive { get; set; }

        public string EmployeeName { get; set; } =
            string.Empty;
    }

    private class SystemUserRecord
    {
        public int Id { get; set; }

        public int? EmployeeId { get; set; }

        public string FullName { get; set; } =
            string.Empty;

        public string UserName { get; set; } =
            string.Empty;

        public string Email { get; set; } =
            string.Empty;

        public int Role { get; set; }

        public bool IsActive { get; set; }

        public string Notes { get; set; } =
            string.Empty;
    }

    private class EmployeeRecord
    {
        public int Id { get; set; }

        public string EmployeeNo { get; set; } =
            string.Empty;

        public string FullName { get; set; } =
            string.Empty;
    }
}
