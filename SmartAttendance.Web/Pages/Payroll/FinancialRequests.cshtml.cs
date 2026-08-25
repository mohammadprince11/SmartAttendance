using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// مركز الطلبات المالية (/Payroll/FinancialRequests) — يوحّد الطلبات المالية (قرض/سُلفة/
/// بدل/استرداد/زيادة راتب) عبر العمود الفقري نفسه: كل طلب يمرّ بلجنة موافقة
/// (<see cref="ApprovalWorkflowEngine"/>) وعند الاعتماد النهائي يُفعَّل أثره المالي عبر
/// <see cref="FinancialRequestStore.ApplyIfFinancialAsync"/>. HR يُنشئ الطلبات نيابةً هنا،
/// ويبتّها خطوةً بخطوة — أو يبتّها من شاشة الموافقات العامة (كلاهما يُفعّل الأثر نفسه).
/// </summary>
public class FinancialRequestsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    private readonly ICompanyScopeProvider _companyScope;

    public FinancialRequestsModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Kind { get; set; }
    [BindProperty] public string? Note { get; set; }

    public List<FinancialRequestStore.Row> Rows { get; set; } = new();
    public List<FinancialRequestStore.EmployeeBasic> Employees { get; set; } = new();

    // KPIs
    public int PendingCount { get; set; }
    public decimal PendingAmount { get; set; }
    public int ApprovedCount { get; set; }

    private string CurrentUser => User.Identity?.Name ?? "system";

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_db);
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        Rows = await FinancialRequestStore.ListAsync(_db, scope, kind: Kind, status: Status, search: Search);
        Employees = await FinancialRequestStore.EmployeeBasicsAsync(_db, scope);

        PendingCount = Rows.Count(r => r.Status == "Pending");
        PendingAmount = Rows.Where(r => r.Status == "Pending").Sum(r => r.Detail.Amount);
        ApprovedCount = Rows.Count(r => r.Status == "Approved");
    }

    public async Task<IActionResult> OnPostSubmitAsync()
    {
        var form = Request.Form;
        var employeeId = int.TryParse(form["EmployeeId"], out var emp) ? emp : 0;
        var kind = form["Kind"].ToString() is { Length: > 0 } k ? k : FinancialRequestStore.Loan;
        var amount = decimal.TryParse(form["Amount"], out var amt) ? amt : 0;

        if (employeeId <= 0 || amount <= 0)
        {
            TempData["SuccessMessage"] = "الموظف والمبلغ مطلوبان.";
            return RedirectToPage();
        }

        var detail = new FinancialRequestStore.Detail
        {
            Kind = kind,
            Amount = amount,
            InstallmentCount = int.TryParse(form["InstallmentCount"], out var cnt) ? Math.Max(1, cnt) : 1,
            StartYear = int.TryParse(form["StartYear"], out var sy) ? sy : DateTime.Today.Year,
            StartMonth = int.TryParse(form["StartMonth"], out var sm) ? sm : DateTime.Today.Month,
            RaiseType = form["RaiseType"].ToString() is { Length: > 0 } rt ? rt : SalaryRaiseStore.ByAmount,
            PaymentType = form["PaymentType"].ToString() is { Length: > 0 } pt ? pt : "InSalary",
            Taxable = form["Taxable"] == "on" || form["Taxable"] == "true",
            Reason = NullIfEmpty(form["Reason"]),
            Note = NullIfEmpty(form["Note"])
        };

        // الموظف من النموذج — HR ينشئ الطلب نيابةً عنه. يجب أن يكون ضمن نطاق المُنشئ
        // وإلّا كان بابَ إنشاء أثرٍ ماليّ لموظف شركةٍ أخرى. مغلق الفشل.
        if (!await EmployeeCompanyGuard.CanAccessEmployeeAsync(
                _db, employeeId, await _companyScope.GetAsync(HttpContext.RequestAborted), HttpContext.RequestAborted))
        {
            TempData["SuccessMessage"] = "الموظف خارج نطاق صلاحيتك.";
            return RedirectToPage();
        }

        var requestId = await FinancialRequestStore.SubmitAsync(_db, detail, employeeId, CurrentUser);
        TempData["SuccessMessage"] = requestId > 0
            ? $"تم إنشاء طلب {FinancialRequestStore.KindLabel(kind)} وإرساله للجنة الموافقة."
            : "تعذّر إنشاء الطلب.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        await HrmsDatabase.EnsureCreatedAsync(_db);
        var result = await ApprovalWorkflowEngine.ApproveAsync(
            _db, await _companyScope.GetAsync(HttpContext.RequestAborted), id, CurrentUser, Note,
            User.Claims.Where(claim => claim.Type == System.Security.Claims.ClaimTypes.Role).Select(claim => claim.Value),
            int.TryParse(User.FindFirst("EmployeeId")?.Value, out var actorEmployeeId) ? actorEmployeeId : null);
        var message = result.Message;

        if (result.FinalApproved)
        {
            var applied = await FinancialRequestStore.ApplyIfFinancialAsync(
                _db, await _companyScope.GetAsync(HttpContext.RequestAborted), id, CurrentUser,
                HttpContext.Connection.RemoteIpAddress?.ToString());
            if (applied) message = "تم الاعتماد النهائي وتفعيل الأثر المالي (قرض/بدل/زيادة حسب النوع).";
        }

        TempData["SuccessMessage"] = message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(int id)
    {
        await HrmsDatabase.EnsureCreatedAsync(_db);
        var result = await ApprovalWorkflowEngine.RejectAsync(
            _db, await _companyScope.GetAsync(HttpContext.RequestAborted), id, CurrentUser, Note,
            User.Claims.Where(claim => claim.Type == System.Security.Claims.ClaimTypes.Role).Select(claim => claim.Value),
            int.TryParse(User.FindFirst("EmployeeId")?.Value, out var actorEmployeeId) ? actorEmployeeId : null);
        TempData["SuccessMessage"] = result.Message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // الطلب المالي صفٌّ بـSelfServiceRequests يخصّ موظفاً — والحذف كان بمعرّفٍ
        // من النموذج بلا فحص. (بوابة الموظف تفحص الملكية أصلاً؛ الباك أوفيس لا.)
        if (!await EmployeeCompanyGuard.CanAccessOwnedRowAsync(
                _db, EmployeeCompanyGuard.Tables.SelfServiceRequests, "Id", id,
                await _companyScope.GetAsync(HttpContext.RequestAborted),
                HttpContext.RequestAborted))
        {
            TempData["SuccessMessage"] = "الطلب غير موجود أو خارج نطاق صلاحيتك.";
            return RedirectToPage();
        }

        var ok = await FinancialRequestStore.DeletePendingAsync(_db, id);
        TempData["SuccessMessage"] = ok ? "تم حذف الطلب المعلّق." : "لا يمكن حذف طلب بُتّ فيه.";
        return RedirectToPage();
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
