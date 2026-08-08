using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// القروض والسُّلف (/Payroll/Loans) — مطابقة ZenHR «Loan Management»: قرض/سُلفة بمبلغ
/// وأقساط شهرية، موافقة، ثم ترحيل الأقساط المستحقة كاقتطاعات آلية للمسير. القائمة تُظهر
/// المبلغ/الأقساط/المدفوع/المتبقّي والحالة؛ النموذج يحسب القسط الشهري تلقائياً.
/// </summary>
public class LoansModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    private readonly Web.Infrastructure.Security.IProtectedFileService _protectedFiles;

    private readonly ICompanyScopeProvider _companyScope;

    public LoansModel(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        Web.Infrastructure.Security.IProtectedFileService protectedFiles, ICompanyScopeProvider companyScope)
    {
        _companyScope = companyScope;
        _protectedFiles = protectedFiles;
        _db = db;
        _env = env;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? LoanType { get; set; }
    [BindProperty(SupportsGet = true)] public int PostYear { get; set; } = DateTime.Today.Year;
    [BindProperty(SupportsGet = true)] public int PostMonth { get; set; } = DateTime.Today.Month;

    public List<LoanStore.Loan_> Loans { get; set; } = new();
    public List<LoanStore.EmployeeBasic> Employees { get; set; } = new();

    private string CurrentUser => User.Identity?.Name ?? "system";

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        Loans = await LoanStore.ListAsync(_db, scope, status: Status, type: LoanType, search: Search);
        Employees = await LoanStore.EmployeeBasicsAsync(_db, scope);
    }

    public async Task<IActionResult> OnPostSaveAsync(IFormFile? attachment)
    {
        var form = Request.Form;
        var loan = new LoanStore.Loan_
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            EmployeeId = int.TryParse(form["EmployeeId"], out var emp) ? emp : 0,
            LoanType = form["LoanType"].ToString() is { Length: > 0 } lt ? lt : LoanStore.Loan,
            Amount = decimal.TryParse(form["Amount"], out var amt) ? amt : 0,
            InstallmentCount = int.TryParse(form["InstallmentCount"], out var cnt) ? Math.Max(1, cnt) : 1,
            StartYear = int.TryParse(form["StartYear"], out var sy) ? sy : DateTime.Today.Year,
            StartMonth = int.TryParse(form["StartMonth"], out var sm) ? sm : DateTime.Today.Month,
            Reason = NullIfEmpty(form["Reason"]),
            Note = NullIfEmpty(form["Note"]),
            Status = form["Status"].ToString() is { Length: > 0 } st ? st : LoanStore.Pending
        };

        if (loan.EmployeeId <= 0 || loan.Amount <= 0)
        {
            TempData["SuccessMessage"] = "الموظف والمبلغ مطلوبان.";
            return RedirectToPage();
        }

        // المرحلة 6: مرفق القرض (عقد/كفالة) خارج wwwroot والقراءة عبر /files.
        if (attachment is { Length: > 0 })
        {
            var stored = await _protectedFiles.SaveAsync(
                attachment, loan.EmployeeId, "loan", HttpContext.RequestAborted);

            if (stored is not null)
            {
                loan.AttachmentName = Path.GetFileName(attachment.FileName);
                loan.AttachmentPath = stored;
            }
        }

        await LoanStore.SaveAsync(_db, loan, CurrentUser);
        TempData["SuccessMessage"] = loan.Id > 0 ? "تم تحديث القرض." : "تم إنشاء القرض.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetStatusAsync(int id, string status)
    {
        // اعتماد القرض كتابةٌ مالية: يبدأ الخصم من راتب الموظف. كان يعمل بمعرّفٍ من
        // النموذج بلا فحص ملكية ⟹ اعتماد/رفض/إغلاق قرض موظفٍ بشركة أخرى.
        if (!await EmployeeCompanyGuard.CanAccessOwnedRowAsync(
                _db, EmployeeCompanyGuard.Tables.EmployeeLoans, "Id", id,
                await _companyScope.GetAsync(HttpContext.RequestAborted),
                HttpContext.RequestAborted))
        {
            TempData["SuccessMessage"] = "القرض غير موجود أو خارج نطاق صلاحيتك.";
            return RedirectToPage();
        }

        await LoanStore.SetStatusAsync(_db, id, status, CurrentUser);
        TempData["SuccessMessage"] = status switch
        {
            LoanStore.Approved => "تم اعتماد القرض — يبدأ الخصم من الشهر المحدّد.",
            LoanStore.Rejected => "تم رفض القرض.",
            LoanStore.Closed => "تم إغلاق القرض.",
            _ => "تم تحديث الحالة."
        };
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await LoanStore.DeleteAsync(_db, id);
        TempData["SuccessMessage"] = "تم حذف القرض (إن لم يُخصم منه قسط).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPostDueAsync()
    {
        var n = await LoanStore.PostDueInstallmentsAsync(_db, PostYear, PostMonth, CurrentUser);
        TempData["SuccessMessage"] = n > 0
            ? $"تم ترحيل {n} قسط كاقتطاع لشهر {PostMonth:00}/{PostYear}."
            : $"لا أقساط مستحقة غير مرحّلة حتى {PostMonth:00}/{PostYear}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetScheduleAsync(int id)
    {
        var rows = await LoanStore.InstallmentsAsync(_db, id);
        return new JsonResult(rows.Select(r => new
        {
            r.SeqNo, r.DueText, r.Amount, r.IsPosted
        }));
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
