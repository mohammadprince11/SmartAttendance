using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// المسير (/Payroll/Runs) — قلب مودل الرواتب: قائمة الدفعات الشهرية بدورة حياة
/// (مسودة ← محتسب ← مقفل ← معتمد ← أُرسلت القسائم)، إنشاء دفعة، والاحتساب والانتقالات.
/// </summary>
public class RunsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public RunsModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public sealed class EmployeeOption
    {
        public int Id { get; set; }
        public string No { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public List<PayrollRunStore.PayrollRun> Runs { get; set; } = new();
    public List<int> AvailableYears { get; set; } = new();

    /// <summary>الموظفون النشطون لمنتقي النطاق (اختيار يدوي) — نفس قائمة الاحتساب.</summary>
    public List<EmployeeOption> Employees { get; set; } = new();
    public List<string> AllDepartments { get; set; } = new();
    public List<string> AllBranches { get; set; } = new();
    public List<string> AllJobTitles { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    public int NewYear { get; set; } = DateTime.Today.Year;
    public int NewMonth { get; set; } = DateTime.Today.Month;

    // مؤشرات
    public int TotalRuns { get; set; }
    public int PaidRuns { get; set; }
    public PayrollRunStore.PayrollRun? LatestRun { get; set; }
    public decimal YearNet { get; set; }
    public decimal YearGross { get; set; }
    public int LatestEmployees { get; set; }

    public async Task OnGetAsync()
    {
        var all = await PayrollRunStore.ListRunsAsync(_db);
        AvailableYears = all.Select(r => r.Year).Distinct().OrderByDescending(y => y).ToList();
        if (AvailableYears.Count == 0) AvailableYears.Add(DateTime.Today.Year);

        TotalRuns = all.Count;
        PaidRuns = all.Count(r => r.Status is "Issued" or "PayslipSent");
        LatestRun = all.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenByDescending(r => r.Id).FirstOrDefault();
        LatestEmployees = LatestRun?.EmployeeCount ?? 0;

        var filterYear = Year ?? AvailableYears.First();
        YearNet = all.Where(r => r.Year == filterYear).Sum(r => r.TotalNet);
        YearGross = all.Where(r => r.Year == filterYear).Sum(r => r.TotalGross);

        Runs = (Year.HasValue ? all.Where(r => r.Year == Year.Value) : all).ToList();

        Employees = await HrmsDatabase.QueryAsync(_db,
            "SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(IsActive,1)=1 ORDER BY EmployeeNo;",
            command => { },
            reader => new EmployeeOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                No = HrmsDatabase.GetString(reader, "EmployeeNo"),
                Name = HrmsDatabase.GetString(reader, "FullName")
            });

        (AllDepartments, AllBranches, AllJobTitles) = await MassScopeResolver.OrgListsAsync(_db);
    }

    public async Task<IActionResult> OnPostCreateAsync(int year, int month, IFormFile? massFile)
    {
        var (scopeMode, ids, error) = await ResolveScopeAsync(massFile);
        if (error != null)
        {
            TempData["PayrollMessage"] = error;
            TempData["PayrollOk"] = false;
            return RedirectToPage();
        }

        var (ok, message, _) = await PayrollRunStore.CreateRunAsync(_db, year, month, scopeMode, ids);
        TempData["PayrollMessage"] = message;
        TempData["PayrollOk"] = ok;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetScopeAsync(int id, IFormFile? massFile)
    {
        var (scopeMode, ids, error) = await ResolveScopeAsync(massFile);
        if (error != null)
        {
            TempData["PayrollMessage"] = error;
            TempData["PayrollOk"] = false;
            return RedirectToPage();
        }

        return await ActAsync(PayrollRunStore.SetScopeAsync(_db, id, scopeMode, ids));
    }

    /// <summary>
    /// معاينة الأكواد الملصوقة قبل التشغيل (JSON): الموجود مقابل المفقود.
    /// الغرض ألا يُكتشف كودٌ مخطئ بعد الاحتساب — حينها يكون موظفٌ غاب عن راتبه.
    /// </summary>
    public async Task<IActionResult> OnPostPreviewCodesAsync(string? codes)
    {
        var (matched, missing) = await PayrollRunScopeStore.PreviewCodesAsync(_db, codes);
        return new JsonResult(new
        {
            matched = matched.Select(m => new { id = m.Id, no = m.No, name = m.Name }),
            missing
        });
    }

    /// <summary>
    /// يحلّ نطاق النموذج. الوضع «الكل» يعيد قائمة فارغة عمداً — لا صفوف نطاق
    /// بالقاعدة لتشغيل يشمل الجميع (الافتراض بالكود لا بالبيانات).
    /// </summary>
    private async Task<(string Mode, List<int> Ids, string? Error)> ResolveScopeAsync(IFormFile? massFile)
    {
        var mode = PayrollRunScope.NormalizeMode(Request.Form["ScopeMode"].ToString());
        if (mode == PayrollRunScope.ModeAll) return (mode, new List<int>(), null);

        var (ids, _, missing, label, error) = await MassScopeResolver.ResolveDetailedAsync(_db, Request.Form, massFile);
        if (error != null) return (mode, ids, error);
        if (ids.Count == 0)
            return (mode, ids, missing.Count > 0
                ? $"لا كود مطابقاً من {missing.Count} — لم يُطبَّق النطاق ({label}). أول المفقود: {string.Join("، ", missing.Take(8))}"
                : $"لم تحدَّد أي موظفين بالنطاق ({label}) — اختر «كل الموظفين» إن كنت تقصد الجميع.");

        if (missing.Count > 0)
            TempData["PayrollMissingCodes"] =
                $"⚠ {missing.Count} كوداً غير مطابق تُخطّي: {string.Join("، ", missing.Take(20))}{(missing.Count > 20 ? " …" : "")}";

        return (mode, ids, null);
    }

    public async Task<IActionResult> OnPostCalculateAsync(int id)
    {
        var (ok, message) = await PayrollRunStore.CalculateAsync(_db, id, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = message;
        TempData["PayrollOk"] = ok;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLockAsync(int id) => await ActAsync(PayrollRunStore.LockAsync(_db, id));
    public async Task<IActionResult> OnPostIssueAsync(int id) => await ActAsync(PayrollRunStore.IssueAsync(_db, id));
    public async Task<IActionResult> OnPostSendAsync(int id) => await ActAsync(PayrollRunStore.SendPayslipsAsync(_db, id));
    public async Task<IActionResult> OnPostReopenAsync(int id) => await ActAsync(PayrollRunStore.ReopenAsync(_db, id));
    public async Task<IActionResult> OnPostDeleteAsync(int id) => await ActAsync(PayrollRunStore.DeleteRunAsync(_db, id));

    private async Task<IActionResult> ActAsync(Task<(bool Ok, string Message)> action)
    {
        var (ok, message) = await action;
        TempData["PayrollMessage"] = message;
        TempData["PayrollOk"] = ok;
        return RedirectToPage();
    }
}
