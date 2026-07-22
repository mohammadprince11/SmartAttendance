using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// تفاصيل دفعة المسير (/Payroll/RunDetail?id=) — سطر لكل موظف بالإجمالي والاستقطاعات
/// والصافي، مع قسيمة تفصيلية (بنود الإضافات والاستقطاعات) لكل موظف.
/// </summary>
public class RunDetailModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public RunDetailModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public PayrollRunStore.PayrollRun? Run { get; set; }
    public List<PayrollRunStore.PayrollLine> Lines { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        Run = await PayrollRunStore.GetRunAsync(_db, Id);
        if (Run == null) return RedirectToPage("Runs");
        Lines = await PayrollRunStore.ListLinesAsync(_db, Id);
        return Page();
    }
}
