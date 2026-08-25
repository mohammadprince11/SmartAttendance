using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Reports;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.EmployeePortal;

/// <summary>
/// التقارير المنشورة للموظف (نظير «مشاركة هذا التقرير مع الموظفين عبر الخدمة
/// الذاتية» بكيان).
///
/// ⚠️ **الفرق الجوهري عن كيان**: عندهم التقرير يُنشَر كما هو. عندنا **مقصورٌ على
/// صفوف الموظف نفسه**. تقرير «معلومات الموظفين» بألف صفّ منشوراً كما هو يعني كشف
/// رواتب الزملاء وهوياتهم بنقرة — والقاعدة عندنا أن كل استعلام موظف يفرض نطاق
/// تخويله. فالنشر هنا يعني «اعرض له سطره» لا «افتح له الجدول».
///
/// والقصْر يتمّ على <c>no</c> (الرقم الوظيفي) لأنه المفتاح الوحيد الحاضر بكل
/// مجموعات البيانات. مجموعةٌ بلا هذا العمود **لا تُعرض إطلاقاً** بدل أن تُعرض
/// كاملة — الفشل الآمن هو الإخفاء لا الكشف.
/// </summary>
[Authorize]
public class ReportsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public ReportsModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public int ReportId { get; set; }

    public List<PeopleReportsStore.SavedReport> Published { get; set; } = new();

    public PeopleReportsStore.SavedReport? Current { get; set; }
    public List<PeopleReportCatalog.ReportColumn> Columns { get; set; } = new();
    public List<Dictionary<string, string>> Rows { get; set; } = new();

    /// <summary>سببُ عدم عرض تقرير مفتوح — يُقال صراحةً بدل جدول فارغ بلا تفسير.</summary>
    public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_dbContext, HttpContext, "ViewPublishedReports")) return Forbid();
        await PeopleReportsStore.EnsureSchemaAsync(_dbContext);

        var employee = await ResolveEmployeeAsync();
        if (employee is null)
        {
            return RedirectToPage("/EmployeePortal/Index");
        }

        var employeeScope = employee.Value.CompanyId is > 0
            ? CompanyScope.ForCompanies(new[] { employee.Value.CompanyId.Value })
            : CompanyScope.DeniedAll();
        var all = await PeopleReportsStore.LoadAllAsync(_dbContext, employeeScope);
        Published = all.Where(report => report.ShareWithEmployees).ToList();

        if (ReportId <= 0)
        {
            return Page();
        }

        // التقرير المطلوب يجب أن يكون **من المنشورة**: تخمين معرّف بالرابط لا يفتح
        // تقريراً غير منشور (IDOR).
        Current = Published.FirstOrDefault(report => report.Id == ReportId);
        if (Current is null)
        {
            Notice = "هذا التقرير غير منشور لك.";
            return Page();
        }

        var dataset = PeopleReportCatalog.GetDataset(Current.DatasetKey);
        if (dataset is null)
        {
            Notice = "مصدر بيانات التقرير غير متاح.";
            return Page();
        }

        if (!dataset.Columns.Any(column => column.Key.Equals("no", StringComparison.OrdinalIgnoreCase)))
        {
            // بلا مفتاح موظف لا يمكن قصْر الصفوف ⟹ لا نعرض شيئاً.
            Notice = "هذا التقرير لا يمكن قصره على بياناتك، فلم يُعرض.";
            return Page();
        }

        Columns = Current.Columns
            .Select(key => dataset.Columns.FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Where(column => column != null)
            .Select(column => column!)
            .ToList();

        if (Columns.Count == 0)
        {
            Columns = dataset.Columns.ToList();
        }

        // نطاق البوابة = شركة الموظف نفسه: لا يقرأ مصدرُ الحضور صفوف شركات أخرى
        // حتى قبل ترشيح «صفوفي أنا» بالأسفل. موظف بلا شركة ⟹ مغلق الفشل.
        var companyId = employee.Value.CompanyId;

        var rows = await PeopleReportCatalog.LoadAsync(
            _dbContext,
            Current.DatasetKey,
            Current.FilterKey,
            new PeopleReportCatalog.ReportFilters
            {
                ActiveOnly = false,
                Scope = companyId is > 0
                    ? SmartAttendance.Web.Infrastructure.Security.CompanyScope.ForCompanies(new[] { companyId.Value })
                    : SmartAttendance.Web.Infrastructure.Security.CompanyScope.DeniedAll()
            });

        Rows = rows
            .Where(row => string.Equals(
                row.GetValueOrDefault("no", ""), employee.Value.EmployeeNo, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Page();
    }

    /// <summary>نفس مسار حسم الموظف ببوابة الموظف: مطالبة <c>EmployeeId</c> وإلا جدول الدخول.</summary>
    private async Task<(int Id, string EmployeeNo, int? CompanyId)?> ResolveEmployeeAsync()
    {
        var employeeId = 0;

        if (int.TryParse(User.FindFirst("EmployeeId")?.Value, out var claimId) && claimId > 0)
        {
            employeeId = claimId;
        }
        else
        {
            var username = User.Identity?.Name;
            if (!string.IsNullOrWhiteSpace(username))
            {
                employeeId = await SmartAttendance.Web.Infrastructure.Hrms.HrmsDatabase.ScalarAsync<int>(
                    _dbContext,
                    "SELECT TOP 1 ISNULL(EmployeeId, 0) FROM AppLoginUsers WHERE Username = @Username AND IsActive = 1",
                    command => SmartAttendance.Web.Infrastructure.Hrms.HrmsDatabase.AddParameter(
                        command, "@Username", username));
            }
        }

        if (employeeId <= 0)
        {
            return null;
        }

        var match = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId && !e.IsDeleted)
            .Select(e => new { e.Id, e.EmployeeNo, e.CompanyId })
            .FirstOrDefaultAsync();

        return match is null ? null : (match.Id, match.EmployeeNo, match.CompanyId);
    }
}
