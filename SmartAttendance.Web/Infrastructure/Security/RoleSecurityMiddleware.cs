using System.Security.Claims;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// حارس المسارات المركزي: يفحص كل طلب ضد دور المستخدم — نظام صلاحيات ديناميكي
/// (PeopleRoutePermissionResolver) مع قوائم توافقية ثابتة لكل دور كخط رجوع.
/// ملاحظة: القوائم الثابتة أدناه هي مصدر الحقيقة لما يراه كل دور من صفحات.
/// </summary>
public class RoleSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly SemaphoreSlim LoginDatabaseEnsureLock = new(1, 1);
    private static volatile bool LoginDatabaseIsReady;

    public RoleSecurityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ApplicationDbContext dbContext,
        ILoginIdentityService loginIdentityService,
        IPermissionAuthorizationService permissionAuthorizationService)
    {
        await EnsureLoginDatabaseCreatedAsync(dbContext);

        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "/";

        if (IsPublicPath(path))
        {
            await _next(context);
            return;
        }

        if (context.User?.Identity?.IsAuthenticated != true)
        {
            RedirectToLogin(context);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var username = context.User.Identity?.Name ??
                       context.User.FindFirstValue(ClaimTypes.Name);
        var role = context.User.FindFirstValue(ClaimTypes.Role);
        var employeeId = context.User.FindFirstValue("EmployeeId");
        var displayName = context.User.FindFirstValue("DisplayName") ?? username;

        if (string.IsNullOrWhiteSpace(userId) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(role))
        {
            RedirectToLogin(context);
            return;
        }

        var systemUserId = await ResolveSystemUserIdAsync(
            context,
            loginIdentityService,
            username,
            displayName ?? username,
            role,
            employeeId);

        PeopleAccessContext.SetSystemUserId(context, systemUserId);

        var isAllowed = await IsAllowedAsync(
            context,
            dbContext,
            permissionAuthorizationService,
            path,
            role,
            employeeId,
            systemUserId);

        if (!isAllowed)
        {
            context.Response.Redirect("/AccessDenied");
            return;
        }

        await _next(context);
    }

    private static async Task EnsureLoginDatabaseCreatedAsync(
        ApplicationDbContext dbContext)
    {
        if (LoginDatabaseIsReady)
        {
            return;
        }

        await LoginDatabaseEnsureLock.WaitAsync();

        try
        {
            if (LoginDatabaseIsReady)
            {
                return;
            }

            await LoginDatabase.EnsureCreatedAsync(dbContext);
            LoginDatabaseIsReady = true;
        }
        finally
        {
            LoginDatabaseEnsureLock.Release();
        }
    }

    private static bool IsPublicPath(string path)
    {
        if (path == "/account/login" ||
            path == "/account/logout" ||
            path == "/accessdenied")
        {
            return true;
        }

        if (path.StartsWith("/css/") ||
            path.StartsWith("/js/") ||
            path.StartsWith("/lib/") ||
            path.StartsWith("/images/") ||
            path.StartsWith("/brand/") ||
            path.StartsWith("/uploads/") ||
            path.StartsWith("/favicon"))
        {
            return true;
        }

        return false;
    }

    private static void RedirectToLogin(HttpContext context)
    {
        var returnUrl = Uri.EscapeDataString(
            context.Request.Path + context.Request.QueryString);
        context.Response.Redirect($"/Account/Login?ReturnUrl={returnUrl}");
    }

    private static async Task<bool> IsAllowedAsync(
        HttpContext context,
        ApplicationDbContext dbContext,
        IPermissionAuthorizationService permissionAuthorizationService,
        string path,
        string role,
        string? employeeId,
        int? systemUserId)
    {
        role = role.Trim();

        var compatibilityAllowed = await IsCompatibilityAllowedAsync(
            context,
            dbContext,
            path,
            role,
            employeeId);

        var requirement = PeopleRoutePermissionResolver.Resolve(context, path);

        if (requirement == null)
        {
            return compatibilityAllowed;
        }

        // Dynamic People permissions fail closed when the synchronized
        // system identity is unavailable. Compatibility access remains in
        // effect only for routes that do not declare a dynamic requirement.
        if (!systemUserId.HasValue || systemUserId.Value <= 0)
        {
            return false;
        }

        return requirement.ScopeMode switch
        {
            PeoplePermissionScopeMode.DataSet =>
                await permissionAuthorizationService.HasPermissionAsync(
                    systemUserId.Value,
                    requirement.PermissionCode,
                    compatibilityAllowed,
                    context.RequestAborted),

            PeoplePermissionScopeMode.Global =>
                await permissionAuthorizationService.HasGlobalPermissionAsync(
                    systemUserId.Value,
                    requirement.PermissionCode,
                    compatibilityAllowed,
                    context.RequestAborted),

            PeoplePermissionScopeMode.Employee =>
                await IsEmployeePermissionAllowedAsync(
                    context,
                    dbContext,
                    permissionAuthorizationService,
                    systemUserId.Value,
                    requirement.PermissionCode,
                    compatibilityAllowed),

            _ => false
        };
    }

    private static async Task<bool> IsEmployeePermissionAllowedAsync(
        HttpContext context,
        ApplicationDbContext dbContext,
        IPermissionAuthorizationService permissionAuthorizationService,
        int systemUserId,
        string permissionCode,
        bool compatibilityAllowed)
    {
        var targetEmployeeId = await PeopleTargetEmployeeResolver.ResolveAsync(
            context,
            dbContext,
            context.RequestAborted);

        if (!targetEmployeeId.HasValue)
        {
            return false;
        }

        return await permissionAuthorizationService.CanAccessEmployeeAsync(
            systemUserId,
            permissionCode,
            targetEmployeeId.Value,
            compatibilityAllowed,
            context.RequestAborted);
    }

    private static async Task<bool> IsCompatibilityAllowedAsync(
        HttpContext context,
        ApplicationDbContext dbContext,
        string path,
        string role,
        string? employeeId)
    {
        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path == "/" ||
            path == "/index" ||
            path.StartsWith("/account/") ||
            path.StartsWith("/settings/"))
        {
            return true;
        }

        if (path.StartsWith("/myprofile"))
        {
            return HasEmployeeId(employeeId);
        }

        if (role.Equals("HR Manager", StringComparison.OrdinalIgnoreCase))
        {
            return IsAny(path,
                "/organization",
                "/organizationsettings",
                "/alerts",
                "/leavebalances",
                "/assetsmanagement",
                "/peoplereports",
                "/employeetasks",
                "/employees",
                "/myprofile",
                "/useraccess",
                "/devices",
                "/shifts",
                "/shifttypes",
                "/attendancesettings",
                "/dayattendance",
                "/shiftrules",
                "/attendancerecommendations",
                "/shiftassignments",
                // الشاشة التشغيلية الأم للمودل: المسارات البديلة
                // (/attendanceprocessing و/attendancecorrections و/attendanceimports)
                // كلها تُعيد التوجيه إليها، فبدونها كانت كلها تنتهي بـ«لا صلاحية».
                "/attendanceoperations",
                // صفحات أُضيفت للمودل لاحقاً ولم تُحدَّث قوائم الأدوار معها
                "/shiftoverrides",
                "/roster",
                "/employeegeolocations",
                "/attendanceviewer",
                "/monthattendance",
                "/employeeshifts",
                "/attendancerecords",
                "/attendanceprocessing",
                "/attendancecorrections",
                "/attendanceimports",
                "/holidays",
                "/leaverequests",
                "/selfservices",
                "/approvals",
                "/auditlogs",
                "/systemmaintenance",
                "/employeepermissions");
        }

        if (role.Equals("HR Officer", StringComparison.OrdinalIgnoreCase))
        {
            return IsAny(path,
                "/organization",
                "/alerts",
                "/leavebalances",
                "/assetsmanagement",
                "/peoplereports",
                "/employeetasks",
                "/employees",
                "/myprofile",
                "/attendancerecords",
                // الشاشة التي تُعيد إليها مسارات المعالجة/التصحيحات/الاستيراد التوجيه
                "/attendanceoperations",
                "/attendanceprocessing",
                "/attendancecorrections",
                "/attendanceimports",
                "/holidays",
                "/leaverequests",
                "/selfservices",
                "/approvals");
        }

        if (role.Equals("Branch Manager", StringComparison.OrdinalIgnoreCase))
        {
            return IsAny(path,
                "/organization",
                "/employees",
                "/myprofile",
                "/attendancerecords",
                // الشاشة التي تُعيد إليها مسارات المعالجة/التصحيحات التوجيه
                "/attendanceoperations",
                "/attendanceprocessing",
                "/attendancecorrections",
                "/leaverequests",
                "/selfservices");
        }

        if (role.Equals("Finance Viewer", StringComparison.OrdinalIgnoreCase))
        {
            return IsAny(path,
                "/organization");
        }

        if (role.Equals("Employee", StringComparison.OrdinalIgnoreCase))
        {
            if (path.StartsWith("/employeeportal"))
            {
                return HasEmployeeId(employeeId);
            }

            if (path.StartsWith("/myprofile"))
            {
                return HasEmployeeId(employeeId);
            }

            if (IsAny(path, "/selfservices", "/leaverequests"))
            {
                return await IsOwnRequestAsync(context, employeeId);
            }

            return false;
        }

        return false;
    }

    private static async Task<int?> ResolveSystemUserIdAsync(
        HttpContext context,
        ILoginIdentityService loginIdentityService,
        string username,
        string displayName,
        string role,
        string? employeeIdClaim)
    {
        var systemUserIdClaim = context.User.FindFirstValue("SystemUserId");

        if (int.TryParse(systemUserIdClaim, out var existingSystemUserId) &&
            existingSystemUserId > 0)
        {
            return existingSystemUserId;
        }

        int? employeeId = null;

        if (int.TryParse(employeeIdClaim, out var parsedEmployeeId) &&
            parsedEmployeeId > 0)
        {
            employeeId = parsedEmployeeId;
        }

        try
        {
            return await loginIdentityService.EnsureSystemUserAsync(
                new LoginIdentityRequest
                {
                    EmployeeId = employeeId,
                    UserName = username,
                    DisplayName = displayName,
                    CompatibilityRole = role,
                    IsActive = true
                },
                context.RequestAborted);
        }
        catch
        {
            // Compatibility access remains available if identity synchronization fails.
            return null;
        }
    }

    private static bool HasEmployeeId(string? employeeIdClaim)
    {
        return !string.IsNullOrWhiteSpace(employeeIdClaim) &&
               int.TryParse(employeeIdClaim, out var id) &&
               id > 0;
    }

    private static bool IsAny(string path, params string[] allowedPrefixes)
    {
        foreach (var allowed in allowedPrefixes)
        {
            if (path == allowed || path.StartsWith(allowed + "/"))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> IsOwnRequestAsync(
        HttpContext context,
        string? employeeIdClaim)
    {
        if (string.IsNullOrWhiteSpace(employeeIdClaim) ||
            !int.TryParse(employeeIdClaim, out var ownEmployeeId) ||
            ownEmployeeId <= 0)
        {
            return false;
        }

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return true;
        }

        if (!context.Request.HasFormContentType)
        {
            return true;
        }

        var form = await context.Request.ReadFormAsync();

        foreach (var key in form.Keys)
        {
            if (!key.Contains("EmployeeId", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = form[key].ToString();

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!int.TryParse(value, out var postedEmployeeId))
            {
                return false;
            }

            if (postedEmployeeId != ownEmployeeId)
            {
                return false;
            }
        }

        return true;
    }

}
