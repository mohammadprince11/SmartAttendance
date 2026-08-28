using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>كتالوج عناصر الراتب (/Payroll/SalaryItems) — نمط كيان «عناصر الراتب».</summary>
public class SalaryItemsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public SalaryItemsModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    public List<SalaryItemStore.SalaryItem> Items { get; set; } = new();
    public sealed record CompanyOption(int Id, string Name);
    public List<CompanyOption> Companies { get; set; } = new();
    [BindProperty(SupportsGet = true)] public int? CompanyId { get; set; }

    public async Task OnGetAsync()
    {
        var scope = await LoadCompaniesAsync();
        Items = await SalaryItemStore.ListAsync(_db, scope, CompanyId);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var form = Request.Form;
        var item = new SalaryItemStore.SalaryItem
        {
            Id = int.TryParse(form["Id"], out var id) ? id : 0,
            CompanyId = int.TryParse(form["CompanyId"], out var companyId) && companyId > 0 ? companyId : null,
            Name = form["Name"].ToString().Trim(),
            NameEn = string.IsNullOrWhiteSpace(form["NameEn"]) ? null : form["NameEn"].ToString().Trim(),
            ItemType = form["ItemType"].ToString() is { Length: > 0 } t ? t : "Income",
            ValueKind = form["ValueKind"].ToString() is { Length: > 0 } v ? v : "Fixed",
            DefaultValue = decimal.TryParse(form["DefaultValue"], out var dv) ? dv : 0,
            Formula = form["ValueKind"].ToString() == "Formula" && !string.IsNullOrWhiteSpace(form["Formula"]) ? form["Formula"].ToString().Trim() : null,
            Taxable = form["Taxable"] == "true",
            GosiEligible = form["GosiEligible"] == "true",
            InGross = form["InGross"] == "true",
            Prorated = form["Prorated"] == "true",
            OvertimeEligible = form["OvertimeEligible"] == "true",
            UnpaidLeaveEligible = form["UnpaidLeaveEligible"] == "true",
            IsActive = form["IsActive"] == "true",
            SortOrder = int.TryParse(form["SortOrder"], out var so) ? so : 0,
            // تبويب «القواعد» (اختياري · فارغ ⟹ بلا أثر على المسير)
            MinValue = decimal.TryParse(form["MinValue"], out var mn) ? mn : null,
            MaxValue = decimal.TryParse(form["MaxValue"], out var mx) ? mx : null,
            ValidFrom = DateOnly.TryParse(form["ValidFrom"], out var vf) ? vf : null,
            ValidTo = DateOnly.TryParse(form["ValidTo"], out var vt) ? vt : null,
            // تبويب «معايير الاستحقاق» — HrConditions مُسلسَل (فارغ ⟹ مؤهّل للجميع)
            EligibilityJson = string.IsNullOrWhiteSpace(form["EligibilityJson"]) ? null : form["EligibilityJson"].ToString()
        };

        if (string.IsNullOrWhiteSpace(item.Name))
        {
            TempData["PayrollMessage"] = "اسم العنصر مطلوب.";
            return RedirectToPage();
        }

        // حارس تهيئة: رفض المدى المقلوب بدل احتسابٍ خاطئ لاحقاً.
        if (item.MinValue is { } lo && item.MaxValue is { } hi && lo > hi)
        {
            TempData["PayrollMessage"] = "القيمة الدنيا يجب ألّا تتجاوز القيمة القصوى.";
            return RedirectToPage();
        }
        if (item.ValidFrom is { } f && item.ValidTo is { } t2 && f > t2)
        {
            TempData["PayrollMessage"] = "تاريخ بداية الصلاحية يجب ألّا يتجاوز تاريخ النهاية.";
            return RedirectToPage();
        }

        var saved = await SalaryItemStore.SaveAsync(
            _db, await _companyScope.GetAsync(HttpContext.RequestAborted), item);
        TempData["PayrollMessage"] = saved
            ? item.Id > 0 ? "تم تحديث العنصر." : "تمت إضافة العنصر."
            : "تعذر الحفظ: الشركة غير محددة أو خارج نطاق صلاحيتك.";
        return RedirectToPage(new { companyId = item.CompanyId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var deleted = await SalaryItemStore.DeleteAsync(
            _db, await _companyScope.GetAsync(HttpContext.RequestAborted), id);
        TempData["PayrollMessage"] = deleted
            ? "تم حذف العنصر."
            : "العنصر غير موجود أو خارج نطاق صلاحيتك أو عنصر نظام محمي.";
        return RedirectToPage(new { companyId = CompanyId });
    }

    private async Task<CompanyScope> LoadCompaniesAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var query = _db.Companies.AsNoTracking().Where(c => c.IsActive && !c.IsDeleted);
        if (!scope.IsUnrestricted)
        {
            var allowed = scope.AllowedCompanyIds.ToArray();
            query = query.Where(c => allowed.Contains(c.Id));
        }
        Companies = await query.OrderBy(c => c.Name)
            .Select(c => new CompanyOption(c.Id, c.Name))
            .ToListAsync(HttpContext.RequestAborted);
        if (CompanyId is > 0 && !Companies.Any(c => c.Id == CompanyId)) CompanyId = null;
        if (CompanyId is null && Companies.Count == 1) CompanyId = Companies[0].Id;
        return scope;
    }
}
