using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// نهاية الخدمة (/Payroll/EndOfService) — تسوية نهائية محكومة بسياسة الشركة.
/// أهلية المكافأة صريحة لكل تسوية، وقيمتها إمّا Policy أو Manual، ثم يضاف بدل رصيد
/// الإجازات والمستحقات الأخرى وتطرح الاقتطاعات. الاعتماد يرحّل الصافي إلى OffCycle Payroll.
/// كل الأرقام تُحتسب بالسيرفر (لا من العميل).
/// </summary>
public class EndOfServiceModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public EndOfServiceModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>التبويب: Draft (قيد التسوية) | Approved (معتمدة).</summary>
    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "Draft";

    public bool IsApproved => Tab == "Approved";

    public List<EndOfServiceStore.Settlement> Items { get; set; } = new();
    public List<EndOfServiceStore.EmployeeInfo> Employees { get; set; } = new();
    public Dictionary<int, EndOfServicePolicy.Policy> CompanyPolicies { get; set; } = new();

    public int TotalCount { get; set; }
    public int DraftCount { get; set; }
    public int ApprovedCount { get; set; }
    public decimal TotalNet { get; set; }

    public async Task OnGetAsync()
    {
        if (Tab != "Approved") Tab = "Draft";

        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var all = await EndOfServiceStore.ListAsync(_db, scope, search: Search);
        DraftCount = all.Count(x => !x.IsApproved);
        ApprovedCount = all.Count(x => x.IsApproved);
        TotalNet = all.Where(x => x.IsApproved).Sum(x => x.NetSettlement);

        Items = all.Where(x => x.IsApproved == IsApproved).ToList();
        TotalCount = Items.Count;

        Employees = await EndOfServiceStore.EmployeeInfosAsync(_db, scope);
        foreach (var companyId in Employees.Select(e => e.CompanyId).Where(id => id > 0).Distinct())
            CompanyPolicies[companyId] = await EndOfServicePolicy.LoadAsync(_db, companyId);
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

        // كل الحسابات المالية بالسيرفر. الأهلية لا تُستنتج من نص سبب الانتهاء:
        // المادة 45 لها استثناءات، لذلك القرار صريح لكل تسوية.
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var employee = (await EndOfServiceStore.EmployeeInfosAsync(_db, scope))
            .FirstOrDefault(item => item.Id == empId);
        if (employee is null)
        {
            TempData["PayrollMessage"] = "لا صلاحية على هذا الموظف.";
            TempData["PayrollOk"] = false;
            return RedirectToPage();
        }

        var eosPolicy = await EndOfServicePolicy.LoadAsync(_db, employee.CompanyId);
        var gratuityEligible = f["GratuityEligible"] == "true";
        var multiplier = Dec("GratuityMultiplier") == 2m ? 2m : 1m;
        var manualGratuity = Math.Max(0m, Dec("ManualGratuityAmount"));

        var years = EndOfServiceStore.YearsOfService(start.Value, end.Value);
        decimal gratuity;
        if (!gratuityEligible)
        {
            gratuity = 0m;
        }
        else if (eosPolicy.AutoCalculationEnabled)
        {
            gratuity = EndOfServiceStore.ComputeGratuity(
                years, lastBasic, eosPolicy.WeeksPerYear, multiplier).Gratuity;
        }
        else
        {
            if (manualGratuity <= 0m)
            {
                TempData["PayrollMessage"] =
                    "الحساب التلقائي لمكافأة نهاية الخدمة غير مفعّل لهذه الشركة. أدخل مبلغ المكافأة يدوياً أو فعّل سياسة الشركة.";
                TempData["PayrollOk"] = false;
                return RedirectToPage();
            }

            gratuity = manualGratuity;
        }

        var payrollPeriod = await EndOfServiceStore.ResolvePayrollPeriodAsync(
            _db, employee.CompanyId, end.Value);
        var rateBasis = await PayrollDivisorPolicy.ResolveForPeriodAsync(
            _db, employee.CompanyId, payrollPeriod.Year, payrollPeriod.Month);
        var dailyRate = PayrollRateBasis.DailyRate(lastBasic, rateBasis.Divisor);
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
            GratuityCalculationMode = !gratuityEligible
                ? "NotEligible"
                : eosPolicy.AutoCalculationEnabled ? "Policy" : "Manual",
            GratuityEligible = gratuityEligible,
            GratuityWeeksPerYear = gratuityEligible && eosPolicy.AutoCalculationEnabled
                ? eosPolicy.WeeksPerYear
                : null,
            GratuityMultiplier = multiplier,
            GratuityBasisAmount = lastBasic,
            LeaveBalanceDays = leaveDays,
            LeaveEncashment = leaveEnc,
            OtherDues = otherDues,
            Deductions = deductions,
            NetSettlement = net,
            Note = string.IsNullOrWhiteSpace(f["Note"]) ? null : f["Note"].ToString().Trim(),
            Status = "Draft"
        };

        try
        {
            await EndOfServiceStore.SaveAsync(_db, scope, s, User?.Identity?.Name ?? "system");
        }
        catch (UnauthorizedAccessException)
        {
            TempData["PayrollMessage"] = "لا صلاحية على هذا الموظف/التسوية.";
            TempData["PayrollOk"] = false;
            return RedirectToPage();
        }
        TempData["PayrollMessage"] = id > 0 ? "تم تحديث التسوية." : $"تمت إضافة التسوية (صافي {net:#,0.##}).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var result = await EndOfServiceStore.ApproveAsync(
            _db, scope, id, User?.Identity?.Name ?? "system");
        TempData["PayrollMessage"] = result.Message;
        TempData["PayrollOk"] = result.Ok;
        return RedirectToPage(new { Tab = result.Ok || result.PostedToPayroll ? "Approved" : "Draft" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (await EndOfServiceStore.IsApprovedAsync(_db, id))
        {
            TempData["PayrollMessage"] = "التسوية معتمدة — لا يمكن حذفها.";
            TempData["PayrollOk"] = false;
            return RedirectToPage(new { Tab = "Approved" });
        }
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        await EndOfServiceStore.DeleteAsync(_db, scope, id);
        TempData["PayrollMessage"] = "تم حذف التسوية.";
        return RedirectToPage();
    }
}
