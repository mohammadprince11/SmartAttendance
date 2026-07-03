using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Security;

public class RoleSecurityMiddleware
{
    private readonly RequestDelegate _next;

    public RoleSecurityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        await LoginDatabase.EnsureCreatedAsync(dbContext);

        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "/";

        if (IsPublicPath(path))
        {
            await _next(context);
            return;
        }

        var userId = context.Request.Cookies["SA.UserId"];
        var username = context.Request.Cookies["SA.UserName"];
        var role = context.Request.Cookies["SA.Role"];
        var employeeId = context.Request.Cookies["SA.EmployeeId"];

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(role))
        {
            RedirectToLogin(context);
            return;
        }

        var isAllowed = await IsAllowedAsync(context, dbContext, path, role, employeeId);

        if (!isAllowed)
        {
            context.Response.Redirect("/AccessDenied");
            return;
        }

        await _next(context);
    }

    private static bool IsPublicPath(string path)
    {
        if (path == "/account/login" || path == "/account/logout" || path == "/accessdenied")
        {
            return true;
        }

        if (path.StartsWith("/css/") ||
            path.StartsWith("/js/") ||
            path.StartsWith("/lib/") ||
            path.StartsWith("/images/") ||
            path.StartsWith("/uploads/") ||
            path.StartsWith("/favicon"))
        {
            return true;
        }

        return false;
    }

    private static void RedirectToLogin(HttpContext context)
    {
        var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
        context.Response.Redirect($"/Account/Login?ReturnUrl={returnUrl}");
    }

    private static async Task<bool> IsAllowedAsync(HttpContext context, ApplicationDbContext dbContext, string path, string role, string? employeeId)
    {
        role = role.Trim();

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
                "/employees",
                "/employeefile",
                "/myprofile",
                "/useraccess",
                "/devices",
                "/shifts",
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
                "/notifications",
                "/systemmaintenance",
                "/employeepermissions",
                "/reports");
        }

        if (role.Equals("HR Officer", StringComparison.OrdinalIgnoreCase))
        {
            return IsAny(path,
                "/organization",
                "/employees",
                "/employeefile",
                "/myprofile",
                "/attendancerecords",
                "/attendanceprocessing",
                "/attendancecorrections",
                "/attendanceimports",
                "/holidays",
                "/leaverequests",
                "/selfservices",
                "/approvals",
                "/reports");
        }

        if (role.Equals("Branch Manager", StringComparison.OrdinalIgnoreCase))
        {
            return IsAny(path,
                "/organization",
                "/employees",
                "/employeefile",
                "/myprofile",
                "/attendancerecords",
                "/attendanceprocessing",
                "/attendancecorrections",
                "/leaverequests",
                "/selfservices",
                "/reports");
        }

        if (role.Equals("Finance Viewer", StringComparison.OrdinalIgnoreCase))
        {
            return IsAny(path,
                "/organization",
                "/reports");
        }

        if (role.Equals("Employee", StringComparison.OrdinalIgnoreCase))
        {
            if (path.StartsWith("/myprofile"))
            {
                return HasEmployeeId(employeeId);
            }

            if (IsAny(path, "/selfservices", "/leaverequests"))
            {
                return await IsOwnRequestAsync(context, employeeId);
            }

            if (path.StartsWith("/employeefile"))
            {
                return await IsOwnEmployeeFileAsync(context, dbContext, employeeId);
            }

            return false;
        }

        return false;
    }

    private static bool HasEmployeeId(string? employeeIdCookie)
    {
        return !string.IsNullOrWhiteSpace(employeeIdCookie) &&
               int.TryParse(employeeIdCookie, out var id) &&
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

    private static async Task<bool> IsOwnRequestAsync(HttpContext context, string? employeeIdCookie)
    {
        if (string.IsNullOrWhiteSpace(employeeIdCookie) || !int.TryParse(employeeIdCookie, out var ownEmployeeId) || ownEmployeeId <= 0)
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

    private static async Task<bool> IsOwnEmployeeFileAsync(HttpContext context, ApplicationDbContext dbContext, string? employeeIdCookie)
    {
        if (string.IsNullOrWhiteSpace(employeeIdCookie) || !int.TryParse(employeeIdCookie, out var employeeId) || employeeId <= 0)
        {
            return false;
        }

        var requestedEmployeeNo = context.Request.Query["EmployeeNo"].ToString();
        var requestedId = context.Request.Query["Id"].ToString();

        if (!string.IsNullOrWhiteSpace(requestedId) && int.TryParse(requestedId, out var idFromQuery))
        {
            return idFromQuery == employeeId;
        }

        if (string.IsNullOrWhiteSpace(requestedEmployeeNo))
        {
            return false;
        }

        var count = await HrmsDatabase.ScalarAsync<int>(
            dbContext,
            "SELECT COUNT(*) FROM Employees WHERE Id = @EmployeeId AND EmployeeNo = @EmployeeNo",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@EmployeeNo", requestedEmployeeNo);
            });

        return count > 0;
    }
}
