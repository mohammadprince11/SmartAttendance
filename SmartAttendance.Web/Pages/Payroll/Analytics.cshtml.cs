using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// لوحة رسوم الرواتب (/Payroll/Analytics) — نظير «رسومات بيانية» بكيان: اتجاه الإجمالي/
/// الصافي شهرياً، حصة الشركة (الضمان)، وتفكيك آخر مسير (أساسي/علاوات/استقطاعات/صافي).
/// كلها مشتقّة من دفعات المسير المحتسبة — لا صيغة جديدة ولا كتابة، عرضٌ فقط.
/// </summary>
public class AnalyticsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public AnalyticsModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    public sealed class CompanyOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public List<CompanyOption> Companies { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    public List<PayrollRunStore.MonthlyPoint> Series { get; set; } = new();
    public PayrollRunStore.PayrollBreakdown Breakdown { get; set; } = new();

    public decimal MaxGross => Series.Count == 0 ? 1m : Math.Max(1m, Series.Max(p => p.Gross));
    public decimal MaxGosi => Series.Count == 0 ? 1m : Math.Max(1m, Series.Max(p => p.GosiCompany));
    public decimal YearGross => Series.Sum(p => p.Gross);
    public decimal YearNet => Series.Sum(p => p.Net);
    public decimal YearGosi => Series.Sum(p => p.GosiCompany);

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);

        var companyQuery = _db.Companies.AsNoTracking().Where(c => c.IsActive && !c.IsDeleted);
        if (!scope.IsUnrestricted)
        {
            var allowed = scope.AllowedCompanyIds.ToArray();
            companyQuery = companyQuery.Where(c => allowed.Contains(c.Id));
        }
        Companies = await companyQuery
            .OrderBy(c => c.Name)
            .Select(c => new CompanyOption { Id = c.Id, Name = c.Name })
            .ToListAsync(HttpContext.RequestAborted);

        if (CompanyId.HasValue && !Companies.Any(c => c.Id == CompanyId.Value)) CompanyId = null;
        if (!CompanyId.HasValue && Companies.Count == 1) CompanyId = Companies[0].Id;

        var company = CompanyId is > 0 ? CompanyId : null;
        Series = await PayrollRunStore.MonthlySeriesAsync(_db, scope, company, 12);
        Breakdown = await PayrollRunStore.LatestBreakdownAsync(_db, scope, company);
    }
}
