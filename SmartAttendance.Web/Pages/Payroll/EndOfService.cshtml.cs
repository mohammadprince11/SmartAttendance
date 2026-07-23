using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// نهاية الخدمة (/Payroll/EndOfService) — مطابقة كيان «نهاية الخدمة/STB». تحسب مكافأة
/// نهاية الخدمة بشرائح سنوات الخدمة على آخر أساسي + بدل رصيد الإجازات + مستحقات −
/// اقتطاعات = صافي التسوية. كل الأرقام تُحتسب بالسيرفر (لا من العميل).
/// </summary>
public class EndOfServiceModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public EndOfServiceModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>التبويب: Draft (قيد التسوية) | Approved (معتمدة).</summary>
    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "Draft";

    public bool IsApproved => Tab == "Approved";

    public List<EndOfServiceStore.Settlement> Items { get; set; } = new();
    public List<EndOfServiceStore.EmployeeInfo> Employees { get; set; } = new();

    public int TotalCount { get; set; }
    public int DraftCount { get; set; }
    public int ApprovedCount { get; set; }
    public decimal TotalNet { get; set; }

    public async Task OnGetAsync()
    {
        if (Tab != "Approved") Tab = "Draft";

        var all = await EndOfServiceStore.ListAsync(_db, search: Search);
        DraftCount = all.Count(x => !x.IsApproved);
        ApprovedCount = all.Count(x => x.IsApproved);
        TotalNet = all.Where(x => x.IsApproved).Sum(x => x.NetSettlement);

        Items = all.Where(x => x.IsApproved == IsApproved).ToList();
        TotalCount = Items.Count;

        Employees = await EndOfServiceStore.EmployeeInfosAsync(_db);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var f = Request.Form;
        DateOnly? D(string key) => DateOnly.TryParse(f[key], out var d) ? d : null;
        decimal Dec(string key) => decimal.TryParse(f[key], out var v) ? v : 0;

        var empId = int.TryParse(f["EmployeeId"], out var e) ? e : 0;
        var start = D("ServiceStartDate");
        var end = D("LastWorkingDate");
        var lastBasic = Dec("LastBasic");
        var leaveDays = Dec("LeaveBalanceDays");
        var otherDues = Dec("OtherDues");
        var deductions = Dec("Deductions");

        if (empId <= 0) { TempData["PayrollMessage"] = "اختر الموظف."; TempData["PayrollOk"] = false; return RedirectToPage(); }
        if (start is null || end is null) { TempData["PayrollMessage"] = "أدخل تاريخ بدء الخدمة وآخر يوم عمل."; TempData["PayrollOk"] = false; return RedirectToPage(); }
        if (end <= start) { TempData["PayrollMessage"] = "آخر يوم عمل يجب أن يكون بعد بدء الخدمة."; TempData["PayrollOk"] = false; return RedirectToPage(); }
        if (lastBasic <= 0) { TempData["PayrollMessage"] = "آخر راتب أساسي يجب أن يكون أكبر من صفر."; TempData["PayrollOk"] = false; return RedirectToPage(); }

        // كل الحسابات بالسيرفر
        var years = EndOfServiceStore.YearsOfService(start.Value, end.Value);
        var (gratuity, _) = EndOfServiceStore.ComputeGratuity(years, lastBasic);
        var dailyRate = Math.Round(lastBasic / 30m, 4);
        var leaveEnc = Math.Round(leaveDays * dailyRate, 2);
        var net = Math.Round(gratuity + leaveEnc + otherDues - deductions, 2);

        var id = int.TryParse(f["Id"], out var sid) ? sid : 0;
        if (id > 0 && await EndOfServiceStore.IsApprovedAsync(_db, id))
        {
            TempData["PayrollMessage"] = "التسوية معتمدة — لا يمكن تعديلها.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Tab = "Approved" });
        }

        var s = new EndOfServiceStore.Settlement
        {
            Id = id,
            EmployeeId = empId,
            ServiceStartDate = start,
            LastWorkingDate = end,
            YearsService = years,
            LastBasic = lastBasic,
            Reason = string.IsNullOrWhiteSpace(f["Reason"]) ? null : f["Reason"].ToString().Trim(),
            GratuityAmount = gratuity,
            LeaveBalanceDays = leaveDays,
            LeaveEncashment = leaveEnc,
            OtherDues = otherDues,
            Deductions = deductions,
            NetSettlement = net,
            Note = string.IsNullOrWhiteSpace(f["Note"]) ? null : f["Note"].ToString().Trim(),
            Status = "Draft"
        };

        await EndOfServiceStore.SaveAsync(_db, s, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = id > 0 ? "تم تحديث التسوية." : $"تمت إضافة التسوية (صافي {net:#,0.##}).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        var ok = await EndOfServiceStore.ApproveAsync(_db, id, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = ok ? "اعتُمدت التسوية." : "تعذّر الاعتماد (ربما معتمدة سابقاً).";
        TempData["PayrollOk"] = ok;
        return RedirectToPage(new { Tab = ok ? "Approved" : "Draft" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (await EndOfServiceStore.IsApprovedAsync(_db, id))
        {
            TempData["PayrollMessage"] = "التسوية معتمدة — لا يمكن حذفها.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Tab = "Approved" });
        }
        await EndOfServiceStore.DeleteAsync(_db, id);
        TempData["PayrollMessage"] = "تم حذف التسوية.";
        return RedirectToPage();
    }
}
