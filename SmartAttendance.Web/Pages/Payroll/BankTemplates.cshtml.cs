using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// قوالب ملفات البنوك (/Payroll/BankTemplates) — إدارة تنسيقات تصدير المسير للبنك:
/// الأعمدة/ترتيبها/رؤوسها والفاصل والترويسة والقالب الافتراضي.
/// </summary>
public class BankTemplatesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public BankTemplatesModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    public List<BankFileTemplateStore.Template> Templates { get; set; } = new();
    public IReadOnlyList<(string Key, string Label)> Fields => BankFileTemplateStore.Fields;
    public IReadOnlyList<(string Key, string Label, string Char)> Delimiters => BankFileTemplateStore.Delimiters;
    public sealed record CompanyOption(int Id, string Name);
    public List<CompanyOption> Companies { get; set; } = new();
    [BindProperty(SupportsGet = true)] public int? CompanyId { get; set; }

    public async Task OnGetAsync()
    {
        var scope = await LoadCompaniesAsync();
        Templates = await BankFileTemplateStore.ListAsync(_db, scope, CompanyId);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var f = Request.Form;
        var columns = f["Columns"].Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()).ToList();
        // الرؤوس المخصصة بترتيب الأعمدة المختارة (مجموعة بـ| من الواجهة، محاذية لـColumns).
        var headersJoined = f["HeadersJoined"].ToString();
        var hasCustomHeaders = headersJoined.Replace("|", "").Trim().Length > 0;

        var tpl = new BankFileTemplateStore.Template
        {
            Id = int.TryParse(f["Id"], out var id) ? id : 0,
            CompanyId = int.TryParse(f["CompanyId"], out var companyId) && companyId > 0 ? companyId : null,
            Name = f["Name"].ToString().Trim(),
            BankName = string.IsNullOrWhiteSpace(f["BankName"]) ? null : f["BankName"].ToString().Trim(),
            Delimiter = f["Delimiter"].ToString() is { Length: > 0 } d ? d : "Comma",
            IncludeHeader = f["IncludeHeader"] == "true" || f["IncludeHeader"] == "on",
            ColumnsCsv = string.Join(",", columns),
            HeadersCsv = hasCustomHeaders ? headersJoined : null,
            IsDefault = f["IsDefault"] == "true" || f["IsDefault"] == "on",
            IsActive = f["IsActive"] == "true" || f["IsActive"] == "on"
        };

        var (ok, message) = await BankFileTemplateStore.SaveAsync(
            _db, await _companyScope.GetAsync(HttpContext.RequestAborted), tpl);
        TempData["BtMessage"] = message;
        TempData["BtOk"] = ok;
        return RedirectToPage(new { companyId = tpl.CompanyId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var deleted = await BankFileTemplateStore.DeleteAsync(
            _db, await _companyScope.GetAsync(HttpContext.RequestAborted), id);
        TempData["BtMessage"] = deleted ? "حُذف القالب." : "القالب غير موجود أو خارج نطاق صلاحيتك.";
        TempData["BtOk"] = deleted;
        return RedirectToPage(new { companyId = CompanyId });
    }

    private async Task<CompanyScope> LoadCompaniesAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var query = _db.Companies.AsNoTracking().Where(company => company.IsActive && !company.IsDeleted);
        if (!scope.IsUnrestricted)
        {
            var allowed = scope.AllowedCompanyIds.ToArray();
            query = query.Where(company => allowed.Contains(company.Id));
        }
        Companies = await query.OrderBy(company => company.Name)
            .Select(company => new CompanyOption(company.Id, company.Name))
            .ToListAsync(HttpContext.RequestAborted);
        if (CompanyId is > 0 && !Companies.Any(company => company.Id == CompanyId)) CompanyId = null;
        if (CompanyId is null && Companies.Count == 1) CompanyId = Companies[0].Id;
        return scope;
    }
}
