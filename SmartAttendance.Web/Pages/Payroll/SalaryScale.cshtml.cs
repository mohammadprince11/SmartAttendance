using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// إعدادات سلم الرواتب (نظير «إعدادات سلم الرواتب» بكيان: الأبعاد الثلاثة + الفئات +
/// المجموعات). المنطق والحرّاس بـ<see cref="SalaryScaleStore"/>؛ هذه الصفحة تهيئة فقط.
/// أسماء الأبعاد نفسها إعدادات (تُسمّى «الفئة/الدرجة/المرتبة» أو ما تشاء الشركة).
///
/// عزل التهيئة بالشركة (نمط 8D–8M): القائمة والحفظ والحذف كلها بنطاق شركات المستخدم
/// عبر <see cref="ConfigTenantScope"/> — المعرّف خارج النطاق «غير موجود».
/// </summary>
public class SalaryScaleModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public SalaryScaleModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    public List<SalaryScaleStore.Grade> Grades { get; private set; } = new();

    [BindProperty] public SalaryScaleStore.Grade Input { get; set; } = new();
    [BindProperty] public string Dim1Label { get; set; } = "الفئة";
    [BindProperty] public string Dim2Label { get; set; } = "الدرجة";
    [BindProperty] public string Dim3Label { get; set; } = "المرتبة";

    [TempData] public string? Message { get; set; }
    [TempData] public bool MessageOk { get; set; } = true;

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        Grades = await SalaryScaleStore.ListAsync(_db, scope);
        var companyId = ConfigTenantScope.OwningCompany(scope);
        Dim1Label = companyId is > 0 ? await HrSettingsStore.GetCompanyAsync(_db, companyId.Value, SalaryScaleStore.KeyDim1Label, "الفئة") : "الفئة";
        Dim2Label = companyId is > 0 ? await HrSettingsStore.GetCompanyAsync(_db, companyId.Value, SalaryScaleStore.KeyDim2Label, "الدرجة") : "الدرجة";
        Dim3Label = companyId is > 0 ? await HrSettingsStore.GetCompanyAsync(_db, companyId.Value, SalaryScaleStore.KeyDim3Label, "المرتبة") : "المرتبة";
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        try
        {
            var (ok, msg) = await SalaryScaleStore.SaveAsync(_db, scope, Input);
            Message = msg; MessageOk = ok;
        }
        catch (Exception ex) when (ex.Message.Contains("DUPLICATE_CODE"))
        {
            Message = $"الكود «{Input.Code}» مستخدم لدرجة أخرى."; MessageOk = false;
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!await SalaryScaleStore.DeleteAsync(_db, scope, id))
            return NotFound();
        Message = "حُذفت الدرجة."; MessageOk = true;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveLabelsAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var companyId = ConfigTenantScope.OwningCompany(scope);
        if (companyId is not > 0) return Forbid();
        var actor = User?.Identity?.Name ?? "system";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await PayrollConfigChangeMonitor.SetAndTrackAsync(_db, companyId.Value, SalaryScaleStore.KeyDim1Label, Dim1Label?.Trim(), actor, ip);
        await PayrollConfigChangeMonitor.SetAndTrackAsync(_db, companyId.Value, SalaryScaleStore.KeyDim2Label, Dim2Label?.Trim(), actor, ip);
        await PayrollConfigChangeMonitor.SetAndTrackAsync(_db, companyId.Value, SalaryScaleStore.KeyDim3Label, Dim3Label?.Trim(), actor, ip);
        Message = "حُفظت أسماء الأبعاد."; MessageOk = true;
        return RedirectToPage();
    }
}
