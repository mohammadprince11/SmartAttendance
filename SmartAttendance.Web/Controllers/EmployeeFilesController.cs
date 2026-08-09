using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Controllers;

/// <summary>
/// المرحلة 6 — نقطة التنزيل المصادَقة الوحيدة لملفات ملف الموظف.
///
/// كل تنزيل يمرّ بأربعة حواجز قبل قراءة أي بايت: مصادقة، وجود الصفّ، صلاحية
/// «عرض ملف الشخص» على <b>الموظف المستهدف بعينه</b> (وهي تطبّق نطاق البيانات
/// بما فيه الشركة/الفرع/القسم)، ثم تطبيع المسار الفيزيائي وتأكيد بقائه داخل
/// الجذر المحمي. والاستجابة بلا تخزين مؤقت وكمرفق لا كصفحة مضمّنة.
/// </summary>
[Route("files")]
[Authorize(AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme)]
public sealed class EmployeeFilesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IPermissionAuthorizationService _permissions;
    private readonly IEffectiveScopeService _effectiveScopeService;
    private readonly IWebHostEnvironment _environment;
    private readonly ProtectedFileService _protectedFiles;

    public EmployeeFilesController(
        ApplicationDbContext db,
        IPermissionAuthorizationService permissions,
        IEffectiveScopeService effectiveScopeService,
        IWebHostEnvironment environment,
        IProtectedFileService protectedFiles)
    {
        _db = db;
        _permissions = permissions;
        _effectiveScopeService = effectiveScopeService;
        _environment = environment;
        _protectedFiles = (ProtectedFileService)protectedFiles;
    }

    /// <summary>
    /// نقطة التنزيل العامة لكل موديولات المرفقات (مستندات، مالية، سجلات، تأديبية،
    /// قروض، مرفقات طلبات). الرمز موقّع بـData Protection فيمنع تبديل الموظف أو
    /// المفتاح، لكن **التخويل يُفحص بعده لا به**: الصلاحية على الموظف المستهدف
    /// تُقيَّم من الخادم بنفس محرك نطاق البيانات.
    /// </summary>
    [HttpGet("download")]
    public async Task<IActionResult> Download([FromQuery(Name = "t")] string? token)
    {
        if (!_protectedFiles.TryUnprotect(token, out var employeeId, out var storageKey))
        {
            return NotFound();
        }

        if (!await CanAccessEmployeeAsync(employeeId))
        {
            await WriteAuditAsync("Employee File Download Denied", employeeId, 0);
            return Forbid();
        }

        if (!ProtectedFileStore.TryResolvePhysicalPath(
                _protectedFiles.ResolveRoot(), storageKey, out var physicalPath) ||
            !System.IO.File.Exists(physicalPath))
        {
            return NotFound();
        }

        await WriteAuditAsync("Employee File Downloaded", employeeId, 0);

        Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        Response.Headers["Pragma"] = "no-cache";

        var extension = Path.GetExtension(physicalPath);
        var stream = System.IO.File.OpenRead(physicalPath);

        return File(
            stream,
            ProtectedFileStore.ContentTypeFor(extension),
            Path.GetFileName(physicalPath));
    }

    [HttpGet("employee-profile/{fileId:int}")]
    public async Task<IActionResult> DownloadProfileFile(int fileId)
    {
        if (fileId <= 0)
        {
            return NotFound();
        }

        var rows = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 1
    Id,
    EmployeeId,
    FileName,
    StoredPath,
    ISNULL(ProtectedKey, '') AS ProtectedKey
FROM EmployeeProfileFiles
WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", fileId),
            reader => new
            {
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                FileName = HrmsDatabase.GetString(reader, "FileName"),
                StoredPath = HrmsDatabase.GetString(reader, "StoredPath"),
                ProtectedKey = HrmsDatabase.GetString(reader, "ProtectedKey")
            });

        var file = rows.FirstOrDefault();

        if (file is null || file.EmployeeId <= 0)
        {
            return NotFound();
        }

        if (!await CanAccessEmployeeAsync(file.EmployeeId))
        {
            await WriteAuditAsync("Employee File Download Denied", file.EmployeeId, fileId);
            return Forbid();
        }

        var physicalPath = ResolvePhysicalPath(file.ProtectedKey, file.StoredPath);

        if (physicalPath is null || !System.IO.File.Exists(physicalPath))
        {
            return NotFound();
        }

        await WriteAuditAsync("Employee File Downloaded", file.EmployeeId, fileId);

        Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        Response.Headers["Pragma"] = "no-cache";

        var extension = Path.GetExtension(physicalPath);
        var downloadName = string.IsNullOrWhiteSpace(file.FileName)
            ? $"file-{fileId}{extension}"
            : Path.GetFileName(file.FileName);

        var stream = System.IO.File.OpenRead(physicalPath);
        return File(stream, ProtectedFileStore.ContentTypeFor(extension), downloadName);
    }

    /// <summary>
    /// المفتاح المحمي هو المسار الوحيد للملفات الجديدة. الصفوف التاريخية تبقى
    /// مقروءة من موضعها القديم تحت wwwroot لكن <b>عبر هذه النقطة فقط</b> — الوصول
    /// المباشر لـ/uploads الحسّاسة لم يعد عاماً.
    /// </summary>
    private string? ResolvePhysicalPath(string protectedKey, string storedPath)
    {
        if (!string.IsNullOrWhiteSpace(protectedKey))
        {
            return ProtectedFileStore.TryResolvePhysicalPath(
                ProtectedFileStore.ResolveRoot(_environment.ContentRootPath),
                protectedKey,
                out var protectedPath)
                ? protectedPath
                : null;
        }

        if (string.IsNullOrWhiteSpace(storedPath) || !storedPath.StartsWith('/'))
        {
            return null;
        }

        var webRoot = string.IsNullOrWhiteSpace(_environment.WebRootPath)
            ? Path.Combine(_environment.ContentRootPath, "wwwroot")
            : _environment.WebRootPath;

        var relative = storedPath.TrimStart('/');

        return ProtectedFileStore.TryResolvePhysicalPath(webRoot, relative, out var legacyPath)
            ? legacyPath
            : null;
    }

    private async Task<bool> CanAccessEmployeeAsync(int employeeId)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var isAdmin = RoleRouteCatalog.IsAdmin(role);

        if (!int.TryParse(User.FindFirstValue("SystemUserId"), out var systemUserId) ||
            systemUserId <= 0)
        {
            // بلا هوية نظام لا يمكن تقييم النطاق ⟹ الأدمن فقط (fail closed).
            return isAdmin;
        }

        // الموظف يرى ملفاته هو فقط؛ الباقي يمرّ بمحرك الصلاحيات ونطاق البيانات.
        if (role.Equals("Employee", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(User.FindFirstValue("EmployeeId"), out var ownEmployeeId) &&
                   ownEmployeeId > 0 &&
                   ownEmployeeId == employeeId;
        }

        // نطاق القواعد (ViewProfile) — يفرض الشركة/الفرع/القسم والاستثناءات.
        var rulesAllows = await _permissions.CanAccessEmployeeAsync(
            systemUserId,
            PeoplePermissionCodes.ViewProfile,
            employeeId,
            isAdmin,
            HttpContext.RequestAborted);

        if (!rulesAllows)
        {
            return false;
        }

        // تقاطع نطاق «موظفي أدوار الوصول» (AND) — نفس النموذج الموحّد المطبَّق على
        // قائمة الأشخاص والمنتقي (P0-1): من نطاق أدواره أضيق لا ينزّل ملف من لا يراه.
        // أدمن/«All» يُحسَم غير مقيَّد فلا يضيّق.
        var accessRoleScope = await _effectiveScopeService.GetEmployeesAccessScopeAsync(
            systemUserId, isAdmin, HttpContext.RequestAborted);

        if (accessRoleScope.IsUnrestricted && !accessRoleScope.HasAnyDenial)
        {
            return true;
        }

        var location = await LoadEmployeeLocationAsync(employeeId);

        return location is not null &&
               accessRoleScope.AllowsEmployee(
                   employeeId, location.CompanyId, location.BranchId, location.DepartmentId);
    }

    /// <summary>موقع الموظف التنظيميّ (شركة عبر الفرع/فرع/قسم) لتقييم نطاق أدوار الوصول.</summary>
    private async Task<EmployeeLocation?> LoadEmployeeLocationAsync(int employeeId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT TOP 1
    ISNULL(b.CompanyId, 0)    AS CompanyId,
    ISNULL(e.BranchId, 0)     AS BranchId,
    ISNULL(e.DepartmentId, 0) AS DepartmentId
FROM Employees e
LEFT JOIN Branches b ON b.Id = e.BranchId
WHERE e.Id = @Id AND ISNULL(e.IsDeleted, 0) = 0;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", employeeId),
            reader => new EmployeeLocation(
                HrmsDatabase.GetInt(reader, "CompanyId"),
                HrmsDatabase.GetInt(reader, "BranchId"),
                HrmsDatabase.GetInt(reader, "DepartmentId")));

        return rows.FirstOrDefault();
    }

    private sealed record EmployeeLocation(int CompanyId, int BranchId, int DepartmentId);

    private async Task WriteAuditAsync(string action, int employeeId, int fileId)
    {
        try
        {
            await HrmsDatabase.ExecuteAsync(
                _db,
                """
INSERT INTO AuditLogs
(EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES
('EmployeeProfileFiles', @EntityId, @Action, @Details, @UserName, @Ip);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EntityId", fileId.ToString());
                    HrmsDatabase.AddParameter(command, "@Action", action);
                    HrmsDatabase.AddParameter(command, "@Details", $"EmployeeId={employeeId}");
                    HrmsDatabase.AddParameter(
                        command,
                        "@UserName",
                        User.Identity?.Name ?? string.Empty);
                    HrmsDatabase.AddParameter(
                        command,
                        "@Ip",
                        HttpContext.Connection.RemoteIpAddress?.ToString());
                });
        }
        catch
        {
            // تعذّر التدقيق لا يمنع/يمرّر وصولاً — القرار اتُّخذ قبله.
        }
    }
}
