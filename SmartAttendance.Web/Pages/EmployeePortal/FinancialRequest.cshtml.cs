using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeePortal;

/// <summary>
/// بوابة الموظف (ESS): تقديم طلب مالي ذاتي (قرض/سُلفة/بدل/استرداد) — يمرّ عبر نفس
/// <see cref="FinancialRequestStore"/> ومحرك الموافقات كطلبات HR، ويُفعَّل أثره عند
/// الاعتماد النهائي. زيادة الراتب مستثناة (لا يطلبها الموظف لنفسه).
/// </summary>
public class FinancialRequestModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public FinancialRequestModel(ApplicationDbContext db) => _db = db;

    [TempData] public string? StatusMessage { get; set; }

    /// <summary>الأنواع المتاحة للموظف ذاتياً (بلا زيادة راتب).</summary>
    public IReadOnlyList<FinancialRequestStore.Kind> Kinds { get; private set; } =
        FinancialRequestStore.Catalog.Where(k => k.Key != FinancialRequestStore.Raise).ToList();

    public List<FinancialRequestStore.Row> MyRequests { get; private set; } = new();
    public bool HasEmployee { get; private set; }

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_db);
        var employeeId = await ResolveEmployeeIdAsync();
        HasEmployee = employeeId > 0;
        if (employeeId > 0)
            MyRequests = (await FinancialRequestStore.ListAsync(_db, SmartAttendance.Web.Infrastructure.Security.CompanyScope.Unrestricted()))
                .Where(r => r.EmployeeId == employeeId).ToList();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن إرسال الطلب لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage();
        }

        var form = Request.Form;
        var kind = form["Kind"].ToString() is { Length: > 0 } k ? k : FinancialRequestStore.Loan;
        if (kind == FinancialRequestStore.Raise) kind = FinancialRequestStore.Loan; // ممنوع ذاتياً.
        var amount = decimal.TryParse(form["Amount"], out var amt) ? amt : 0;

        if (amount <= 0)
        {
            StatusMessage = "المبلغ مطلوب.";
            return RedirectToPage();
        }

        var detail = new FinancialRequestStore.Detail
        {
            Kind = kind,
            Amount = amount,
            InstallmentCount = int.TryParse(form["InstallmentCount"], out var cnt) ? Math.Max(1, cnt) : 1,
            StartYear = int.TryParse(form["StartYear"], out var sy) ? sy : DateTime.Today.Year,
            StartMonth = int.TryParse(form["StartMonth"], out var sm) ? sm : DateTime.Today.Month,
            PaymentType = kind == FinancialRequestStore.Reimbursement ? "OutSalary" : "InSalary",
            Reason = string.IsNullOrWhiteSpace(form["Reason"]) ? null : form["Reason"].ToString().Trim()
        };

        var requestId = await FinancialRequestStore.SubmitAsync(_db, detail, employeeId, User.Identity?.Name ?? "Employee");
        StatusMessage = requestId > 0
            ? $"تم إرسال طلب {FinancialRequestStore.KindLabel(kind)} وهو الآن قيد المراجعة."
            : "تعذّر إرسال الطلب.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        // تأكّد أن الطلب لهذا الموظف قبل الحذف.
        if (employeeId > 0)
        {
            var mine = (await FinancialRequestStore.ListAsync(_db, SmartAttendance.Web.Infrastructure.Security.CompanyScope.Unrestricted())).Any(r => r.RequestId == id && r.EmployeeId == employeeId);
            if (mine) await FinancialRequestStore.DeletePendingAsync(_db, id);
        }
        StatusMessage = "تم حذف الطلب المعلّق.";
        return RedirectToPage();
    }

    private async Task<int> ResolveEmployeeIdAsync()
    {
        var employeeIdClaim = User.FindFirstValue("EmployeeId");
        if (int.TryParse(employeeIdClaim, out var claimEmployeeId) && claimEmployeeId > 0)
            return claimEmployeeId;

        var username = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(username))
            return await HrmsDatabase.ScalarAsync<int>(
                _db,
                "SELECT TOP 1 ISNULL(EmployeeId, 0) FROM AppLoginUsers WHERE Username = @Username AND IsActive = 1",
                command => HrmsDatabase.AddParameter(command, "@Username", username));
        return 0;
    }
}
