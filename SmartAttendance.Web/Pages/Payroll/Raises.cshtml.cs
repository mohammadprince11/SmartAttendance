using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// زيادات الراتب (/Payroll/Raises) — مطابقة كيان «زيادة الراتب». تغيير دائم للأساسي
/// (بمبلغ أو نسبة) بأثر تاريخي؛ عند «التطبيق» يُحدَّث EmployeeFinancialInfos.BasicSalary.
/// الأساسي القديم/الجديد يُحتسبان بالسيرفر من الراتب الحالي للموظف لا من العميل.
/// </summary>
public class RaisesModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public RaisesModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Emp { get; set; }

    /// <summary>التبويب: Pending (غير مُطبَّقة) | Applied (مُطبَّقة).</summary>
    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "Pending";

    public bool IsApplied => Tab == "Applied";

    public List<SalaryRaiseStore.Raise> Items { get; set; } = new();
    public List<SalaryRaiseStore.EmployeeBasic> Employees { get; set; } = new();

    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int AppliedCount { get; set; }
    public decimal TotalIncrease { get; set; }

    public async Task OnGetAsync()
    {
        if (Tab != "Applied") Tab = "Pending";

        var all = await SalaryRaiseStore.ListAsync(_db, Emp, search: Search);
        PendingCount = all.Count(x => !x.IsApplied);
        AppliedCount = all.Count(x => x.IsApplied);
        TotalIncrease = all.Where(x => x.IsApplied).Sum(x => x.Increase);

        Items = all.Where(x => x.IsApplied == IsApplied).ToList();
        TotalCount = Items.Count;

        Employees = await SalaryRaiseStore.EmployeeBasicsAsync(_db);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var f = Request.Form;
        DateOnly? D(string key) => DateOnly.TryParse(f[key], out var d) ? d : null;

        var empId = int.TryParse(f["EmployeeId"], out var e) ? e : 0;
        var type = f["RaiseType"].ToString() == SalaryRaiseStore.ByPercentage ? SalaryRaiseStore.ByPercentage : SalaryRaiseStore.ByAmount;
        var value = decimal.TryParse(f["RaiseValue"], out var v) ? v : 0;

        if (empId <= 0) { TempData["PayrollMessage"] = "اختر الموظف."; TempData["PayrollOk"] = false; return RedirectToPage(); }
        if (value <= 0) { TempData["PayrollMessage"] = "قيمة الزيادة يجب أن تكون أكبر من صفر."; TempData["PayrollOk"] = false; return RedirectToPage(); }

        // الأساسي الحالي من السيرفر (لا من العميل)
        var basics = await SalaryRaiseStore.EmployeeBasicsAsync(_db);
        var oldBasic = basics.FirstOrDefault(x => x.Id == empId)?.Basic ?? 0;
        var newBasic = type == SalaryRaiseStore.ByPercentage
            ? Math.Round(oldBasic * (1 + value / 100m), 2)
            : Math.Round(oldBasic + value, 2);

        var id = int.TryParse(f["Id"], out var rid) ? rid : 0;
        if (id > 0 && await SalaryRaiseStore.IsAppliedAsync(_db, id))
        {
            TempData["PayrollMessage"] = "الزيادة مُطبَّقة — لا يمكن تعديلها.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Tab = "Applied" });
        }

        var raise = new SalaryRaiseStore.Raise
        {
            Id = id,
            EmployeeId = empId,
            OldBasic = oldBasic,
            RaiseType = type,
            RaiseValue = value,
            NewBasic = newBasic,
            EffectiveDate = D("EffectiveDate"),
            Reason = string.IsNullOrWhiteSpace(f["Reason"]) ? null : f["Reason"].ToString().Trim(),
            Note = string.IsNullOrWhiteSpace(f["Note"]) ? null : f["Note"].ToString().Trim(),
            Status = f["Status"].ToString() is { Length: > 0 } st ? st : "Approved"
        };

        await SalaryRaiseStore.SaveAsync(_db, raise, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = id > 0 ? "تم تحديث الزيادة." : $"تمت إضافة الزيادة (الأساسي {oldBasic:#,0.##} ← {newBasic:#,0.##}).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApplyAsync(int id)
    {
        var ok = await SalaryRaiseStore.ApplyAsync(_db, id, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = ok ? "طُبّقت الزيادة وحُدّث الراتب الأساسي." : "تعذّر تطبيق الزيادة (ربما مُطبَّقة سابقاً).";
        TempData["PayrollOk"] = ok;
        return RedirectToPage(new { Tab = ok ? "Applied" : "Pending" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (await SalaryRaiseStore.IsAppliedAsync(_db, id))
        {
            TempData["PayrollMessage"] = "الزيادة مُطبَّقة — لا يمكن حذفها.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Tab = "Applied" });
        }
        await SalaryRaiseStore.DeleteAsync(_db, id);
        TempData["PayrollMessage"] = "تم حذف الزيادة.";
        return RedirectToPage();
    }
}
