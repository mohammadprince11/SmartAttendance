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

    public List<PayrollRunStore.PayrollRun> Runs { get; set; } = new();

    public int NewYear { get; set; } = DateTime.Today.Year;
    public int NewMonth { get; set; } = DateTime.Today.Month;

    public async Task OnGetAsync()
    {
        Runs = await PayrollRunStore.ListRunsAsync(_db);
    }

    public async Task<IActionResult> OnPostCreateAsync(int year, int month)
    {
        var (ok, message, _) = await PayrollRunStore.CreateRunAsync(_db, year, month);
        TempData["PayrollMessage"] = message;
        TempData["PayrollOk"] = ok;
        return RedirectToPage();
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
