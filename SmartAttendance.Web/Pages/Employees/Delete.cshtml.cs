using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// الحذف الإداريّ — إزالة سجلٍّ **باطلٍ** (أُنشئ خطأً) بلا أثرٍ تشغيليّ.
///
/// <para>يختلف عن إنهاء الخدمة: الأخير يُبقي الموظف وتاريخه بتاريخ/سبب/نوع، وهو
/// المسار الصحيح لموظفٍ حقيقيٍّ ترك العمل. هنا: إن وُجد أيّ أثرٍ تشغيليّ (حضور ·
/// رواتب · عقود · إجازات · قروض · طلبات · مستندات · مخالفات · إنهاء خدمة) يُرفض
/// الحذف ويُوجَّه المستخدم لإنهاء الخدمة — <b>لا تحويل تلقائيّ</b> (الإنهاء يلزمه
/// تاريخٌ وسببٌ ونوعٌ يقرّرها الإنسان).</para>
///
/// <para>الصلاحية <see cref="PeoplePermissionCodes.AdministrativeDelete"/> مخصّصةٌ
/// لا يمنحها تفويض الإنهاء العاديّ ولا توافقية الأدوار الحسّاسة — فحصٌ داخل الصفحة
/// دفاعاً عن الوسيط (الذي يمنح التوافقية بعضوية بادئة المسار).</para>
/// </summary>
public class DeleteModel : PageModel
{
    private readonly IEmployeeService _employeeService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IPermissionAuthorizationService _permissionAuthorizationService;

    public DeleteModel(
        IEmployeeService employeeService,
        ApplicationDbContext dbContext,
        IPermissionAuthorizationService permissionAuthorizationService)
    {
        _employeeService = employeeService;
        _dbContext = dbContext;
        _permissionAuthorizationService = permissionAuthorizationService;
    }

    public EmployeeDetailsViewModel Employee { get; set; } = new();

    public string? ErrorMessage { get; set; }

    /// <summary>هل للموظف أثرٌ تشغيليّ يمنع الحذف الإداريّ؟ (يعطّل الزرّ ويشرح البديل).</summary>
    public bool HasOperationalHistory { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var employee = await _employeeService.GetByIdAsync(id);

        if (employee == null)
        {
            return NotFound();
        }

        Employee = employee;
        HasOperationalHistory = await EmployeeOperationalHistoryGuard.HasAnyAsync(_dbContext, id);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        var employee = await _employeeService.GetByIdAsync(id);

        if (employee == null)
        {
            ErrorMessage = "لم يتم العثور على الموظف.";
            return Page();
        }

        Employee = employee;

        // فحصٌ داخل الصفحة بالصلاحية المخصّصة: الوسيط يمنح التوافقية بعضوية بادئة المسار
        // (‎/employees‎) فلا يميّز الحذف الإداريّ الحسّاس؛ هذا الفحص هو الذي يقصره على
        // (Admin · HR Manager) أو تخصيصٍ صريح، ويرفض HR Officer/Branch Manager.
        if (!await CanAdministrativelyDeleteAsync(id))
        {
            return Redirect("/AccessDenied");
        }

        // حارس التاريخ التشغيليّ: سجلٌّ له أثرٌ لا يُحذف إدارياً — يُنهى خدمةً.
        var blockingTable = await EmployeeOperationalHistoryGuard.FindFirstNonEmptyAsync(_dbContext, id);
        if (blockingTable is not null)
        {
            HasOperationalHistory = true;
            ErrorMessage =
                "لا يمكن الحذف الإداريّ: للموظف تاريخ تشغيليّ (حضور/رواتب/عقود/طلبات...). مرّ عبر إنهاء الخدمة للحفاظ على التاريخ.";
            return Page();
        }

        // سجلٌّ باطلٌ نظيف: حذفٌ ناعمٌ (IsDeleted=1) يزيله من كل القوائم، مع تدقيق.
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            @"
UPDATE Employees
SET IsActive = 0, IsDeleted = 1
WHERE Id = @Id;

IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
BEGIN
    INSERT INTO AuditLogs (EntityName, EntityId, Action, OldValues, NewValues, UserName, IpAddress)
    VALUES
    (
        'Employee',
        CAST(@Id AS nvarchar(80)),
        'Employee Administratively Deleted',
        @OldValues,
        @NewValues,
        @UserName,
        @IpAddress
    );
END;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", id);
                HrmsDatabase.AddParameter(command, "@OldValues", "IsDeleted: False");
                HrmsDatabase.AddParameter(command, "@NewValues", "IsDeleted: True");
                HrmsDatabase.AddParameter(command, "@UserName", User.Identity?.Name ?? "System");
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });

        TempData["SuccessMessage"] = "تم الحذف الإداريّ للسجل الباطل.";

        return RedirectToPage("./Index");
    }

    private async Task<bool> CanAdministrativelyDeleteAsync(int employeeId)
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);

        return await _permissionAuthorizationService.CanAccessEmployeeAsync(
            systemUserId,
            PeoplePermissionCodes.AdministrativeDelete,
            employeeId,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.AdministrativeDelete),
            HttpContext.RequestAborted);
    }
}
