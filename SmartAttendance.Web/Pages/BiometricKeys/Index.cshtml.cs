using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.BiometricKeys;

/// <summary>
/// اعتماد مفاتيح البصمة/الوجه (/BiometricKeys — للموارد البشرية): كل مفتاح يسجّله
/// موظف يبقى «معلّقاً» حتى يُعتمَد هنا (حاجز مشاركة الحسابات: HR يوثّق أن المفتاح
/// لصاحب الحساب فعلاً). الاعتماد يفعّل المفتاح ويُزيح أي نشط سابق (مفتاح واحد)،
/// والإلغاء يبطل مفتاح جهاز مفقود/موظف منتهٍ.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ICompanyScopeProvider _companyScope;

    public IndexModel(ApplicationDbContext dbContext, ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    public List<WebAuthnCredentialStore.Credential> Credentials { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public int PendingCount { get; set; }
    public int ActiveCount { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        var all = await WebAuthnCredentialStore.ListAllAsync(_dbContext);

        // حصر السرد على موظفي شركاتي — المتجر يسرد مفاتيح كل الشركات (اسم/رقم موظف).
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!scope.IsUnrestricted)
        {
            var allowed = await EmployeeCompanyGuard.FilterEmployeesInScopeAsync(
                _dbContext, all.Select(c => c.EmployeeId), scope, HttpContext.RequestAborted);
            all = all.Where(c => allowed.Contains(c.EmployeeId)).ToList();
        }

        PendingCount = all.Count(c => c.Status == WebAuthnCredentialStore.StatusPending);
        ActiveCount = all.Count(c => c.Status == WebAuthnCredentialStore.StatusActive);

        Credentials = all;
        if (!string.IsNullOrWhiteSpace(Status))
            Credentials = Credentials.Where(c => c.Status == Status).ToList();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var v = Search.Trim();
            Credentials = Credentials.Where(c =>
                c.EmployeeName.Contains(v, StringComparison.OrdinalIgnoreCase) ||
                c.EmployeeNo.Contains(v, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    private string DecidedBy => User.Identity?.Name ?? "hr";

    /// <summary>هل مفتاح البصمة (<paramref name="id"/>) يخصّ موظفاً ضمن شركاتي؟
    /// بدونه كان مدير شركة A يعتمد/يلغي مفاتيح موظفي B بمعرّفٍ مباشر.</summary>
    private async Task<bool> CanAccessCredentialAsync(int id) =>
        await EmployeeCompanyGuard.CanAccessOwnedRowAsync(
            _dbContext, EmployeeCompanyGuard.Tables.EmployeeWebAuthnCredentials, "Id", id,
            await _companyScope.GetAsync(HttpContext.RequestAborted), HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        if (!await CanAccessCredentialAsync(id)) return NotFound();
        var ok = await WebAuthnCredentialStore.ApproveAsync(_dbContext, id, DecidedBy);
        StatusMessage = ok
            ? "تم اعتماد المفتاح — صار نشطاً ويُستخدم للبصم والدخول."
            : "تعذّر الاعتماد (المفتاح ليس بحالة «معلّق»).";
        return RedirectToPage(new { Status, Search });
    }

    public async Task<IActionResult> OnPostRejectAsync(int id)
    {
        if (!await CanAccessCredentialAsync(id)) return NotFound();
        var ok = await WebAuthnCredentialStore.RejectAsync(_dbContext, id, DecidedBy);
        StatusMessage = ok ? "تم رفض المفتاح." : "تعذّر الرفض (المفتاح ليس بحالة «معلّق»).";
        return RedirectToPage(new { Status, Search });
    }

    public async Task<IActionResult> OnPostRevokeAsync(int id)
    {
        if (!await CanAccessCredentialAsync(id)) return NotFound();
        var ok = await WebAuthnCredentialStore.RevokeAsync(_dbContext, id, DecidedBy);
        StatusMessage = ok ? "تم إلغاء المفتاح — لم يعد يعمل للبصم ولا للدخول." : "تعذّر الإلغاء.";
        return RedirectToPage(new { Status, Search });
    }
}
