using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// كشف الرواتب (/Payroll/SalarySheet) — نظير تقرير «كشف المرتبات» بكيان: اختر دفعة مسير
/// فيظهر كل موظفيها مجمّعين بالقسم (أساسي/علاوات/إجمالي/ضريبة/ضمان/صافي) بمجاميع فرعية
/// ومجموع كلّي، قابل للطباعة والتصدير CSV. قراءةٌ فقط، مقيَّد بنطاق شركات المستخدم.
/// </summary>
public class SalarySheetModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public SalarySheetModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    public List<PayrollRunStore.PayrollRun> Runs { get; set; } = new();
    public PayrollRunStore.PayrollRun? Run { get; set; }
    public List<PayrollRunStore.PayrollLine> Lines { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? RunId { get; set; }

    private async Task<PayrollRunStore.PayrollRun?> LoadAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var all = await PayrollRunStore.ListRunsAsync(_db, scope);
        Runs = all.Where(r => r.EmployeeCount > 0)
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ThenByDescending(r => r.Id)
            .ToList();

        if (RunId is not > 0) return null;
        var run = Runs.FirstOrDefault(r => r.Id == RunId.Value); // ضمن النطاق فقط
        if (run == null) { RunId = null; return null; }
        Lines = await PayrollRunStore.ListLinesAsync(_db, run.Id);
        return run;
    }

    public async Task OnGetAsync()
    {
        Run = await LoadAsync();
    }

    /// <summary>تصدير الكشف CSV (يفتح بإكسل). BOM ليعرض العربية صحيحة.</summary>
    public async Task<IActionResult> OnGetExportAsync()
    {
        Run = await LoadAsync();
        if (Run == null) return RedirectToPage(new { RunId });

        var sb = new StringBuilder();
        sb.AppendLine("الرقم,الاسم,القسم,الأساسي,العلاوات,الإجمالي,الضريبة,ضمان الموظف,استقطاعات أخرى,الصافي");
        foreach (var l in Lines.OrderBy(l => l.Department).ThenBy(l => l.EmployeeNo))
        {
            sb.Append(Csv(l.EmployeeNo)).Append(',').Append(Csv(l.EmployeeName)).Append(',')
              .Append(Csv(l.Department)).Append(',').Append(l.BasicSalary).Append(',')
              .Append(l.TotalAllowances).Append(',').Append(l.GrossSalary).Append(',')
              .Append(l.TaxAmount).Append(',').Append(l.GosiEmployee).Append(',')
              .Append(l.OtherDeductions).Append(',').Append(l.NetSalary).AppendLine();
        }
        sb.Append("الإجمالي,,,")
          .Append(Lines.Sum(l => l.BasicSalary)).Append(',').Append(Lines.Sum(l => l.TotalAllowances)).Append(',')
          .Append(Lines.Sum(l => l.GrossSalary)).Append(',').Append(Lines.Sum(l => l.TaxAmount)).Append(',')
          .Append(Lines.Sum(l => l.GosiEmployee)).Append(',').Append(Lines.Sum(l => l.OtherDeductions)).Append(',')
          .Append(Lines.Sum(l => l.NetSalary)).AppendLine();

        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"SalarySheet-{Run.BatchNo}.csv");
    }

    private static string Csv(string? v)
    {
        v ??= string.Empty;
        return v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;
    }
}
