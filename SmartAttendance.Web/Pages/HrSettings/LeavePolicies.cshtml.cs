using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.HrSettings;

[Authorize(Roles = RoleRouteCatalog.Admin)]
public class LeavePoliciesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public LeavePoliciesModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    public sealed record Option(int Id, string Name);

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }
    public List<Option> Companies { get; private set; } = new();
    public List<CompanyLeavePolicyStore.Policy> Policies { get; private set; } = new();
    public string? StatusMessage => TempData["LeavePolicyStatus"]?.ToString();

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        Companies = (await _db.Companies.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new Option(x.Id, x.Name))
            .ToListAsync())
            .Where(x => scope.Allows(x.Id)).ToList();

        CompanyId = CompanySelectionContext.Resolve(
            HttpContext, CompanyId, Companies.Select(x => x.Id).ToArray());
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return;
        Policies = await CompanyLeavePolicyStore.ListForCompanyAsync(_db, CompanyId.Value, onlyActive: true);
    }

    public async Task<IActionResult> OnPostSaveAsync(
        int companyId, int requestTypeId, bool requiresBalance, decimal? entitlementAmount,
        string balanceUnit, int? balanceSourceRequestTypeId, bool allowNegative,
        decimal? negativeLimitAmount, decimal hoursPerDayOverride, decimal? maxPerRequestAmount,
        decimal? maxPerYearAmount, int minimumNoticeDays, int eligibilityDays,
        bool carryForwardEnabled, decimal? carryForwardMaxAmount, int? carryForwardExpiryMonths,
        string accrualMethod, bool prorateOnHire, bool allowRetroactive, bool reasonRequired,
        string attachmentMode)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (companyId <= 0 || requestTypeId <= 0 || !scope.Allows(companyId)) return Forbid();

        bool? attachmentOverride = attachmentMode switch
        {
            "Required" => true,
            "Optional" => false,
            _ => null
        };

        var policy = new CompanyLeavePolicyStore.Policy
        {
            CompanyId = companyId,
            RequestTypeId = requestTypeId,
            RequiresBalance = requiresBalance,
            EntitlementAmount = entitlementAmount,
            BalanceUnit = balanceUnit,
            BalanceSourceRequestTypeId = balanceSourceRequestTypeId is > 0 && balanceSourceRequestTypeId != requestTypeId
                ? balanceSourceRequestTypeId : null,
            AllowNegative = allowNegative,
            NegativeLimitAmount = negativeLimitAmount,
            HoursPerDayOverride = hoursPerDayOverride,
            MaxPerRequestAmount = maxPerRequestAmount,
            MaxPerYearAmount = maxPerYearAmount,
            MinimumNoticeDays = minimumNoticeDays,
            EligibilityDays = eligibilityDays,
            CarryForwardEnabled = carryForwardEnabled,
            CarryForwardMaxAmount = carryForwardMaxAmount,
            CarryForwardExpiryMonths = carryForwardExpiryMonths,
            AccrualMethod = accrualMethod,
            ProrateOnHire = prorateOnHire,
            AllowRetroactive = allowRetroactive,
            ReasonRequired = reasonRequired,
            AttachmentRequiredOverride = attachmentOverride
        };

        try
        {
            await CompanyLeavePolicyStore.SaveAsync(_db, policy, User.Identity?.Name ?? "HR");
            TempData["LeavePolicyStatus"] = "تم حفظ سياسة الإجازة للشركة.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["LeavePolicyStatus"] = ex.Message;
        }

        return RedirectToPage(new { CompanyId = companyId });
    }
}
