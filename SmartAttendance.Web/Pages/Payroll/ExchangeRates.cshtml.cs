using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

public class ExchangeRatesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public ExchangeRatesModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db; _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)] public int? CompanyId { get; set; }
    [BindProperty(SupportsGet = true, Name = "edit")] public int? EditId { get; set; }
    [BindProperty] public CurrencyExchangeRateStore.RateRow Input { get; set; } = new()
    {
        EffectiveDate = DateOnly.FromDateTime(DateTime.Today), IsActive = true
    };
    public List<CompanyOption> Companies { get; private set; } = new();
    public List<CurrencyExchangeRateStore.RateRow> Rates { get; private set; } = new();
    public string? PayrollCurrency { get; private set; }
    [TempData] public string? Message { get; set; }
    [TempData] public bool MessageOk { get; set; } = true;
    public sealed record CompanyOption(int Id, string Name, string? CurrencyCode);

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        await LoadCompaniesAsync(scope);
        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value)) return;
        Rates = await CurrencyExchangeRateStore.ListAsync(_db, scope, CompanyId.Value);
        PayrollCurrency = Companies.FirstOrDefault(company => company.Id == CompanyId)?.CurrencyCode;
        if (EditId is > 0 && Rates.FirstOrDefault(rate => rate.Id == EditId) is { } current)
            Input = current;
        else
            Input.CompanyId = CompanyId.Value;
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 || Input.CompanyId != CompanyId || !scope.Allows(CompanyId.Value))
            return NotFound();
        try
        {
            var result = await CurrencyExchangeRateStore.SaveAsync(
                _db, scope, Input, User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString());
            Message = result.Message; MessageOk = result.Ok;
        }
        catch (Exception ex) when (ex.Message.Contains("UQ_CurrencyExchangeRates", StringComparison.OrdinalIgnoreCase) ||
                                   ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            Message = "يوجد سعر لنفس زوج العملات وتاريخ السريان؛ عدّل السجل الموجود."; MessageOk = false;
        }
        return RedirectToPage(new { CompanyId });
    }

    public async Task<IActionResult> OnPostDeactivateAsync(int id)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (CompanyId is not > 0 ||
            !await CurrencyExchangeRateStore.DeactivateAsync(_db, scope, CompanyId.Value, id, User.Identity?.Name))
            return NotFound();
        Message = "عُطّل السعر؛ المسيرات المحتسبة سابقاً تحتفظ بلقطة السعر الذي استعملته.";
        return RedirectToPage(new { CompanyId });
    }

    private async Task LoadCompaniesAsync(CompanyScope scope)
    {
        var query = _db.Companies.AsNoTracking().Where(company => !company.IsDeleted && company.IsActive);
        if (!scope.IsUnrestricted)
        {
            var allowed = scope.AllowedCompanyIds.ToArray();
            query = query.Where(company => allowed.Contains(company.Id));
        }
        Companies = await query.OrderBy(company => company.Name)
            .Select(company => new CompanyOption(company.Id, company.Name, company.CurrencyCode)).ToListAsync();
        if (!CompanyId.HasValue && Companies.Count == 1) CompanyId = Companies[0].Id;
    }
}
