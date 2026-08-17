using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// مدقق حركات الرواتب (نظير «مدقق حركات كيان»): ملخص لكل موظف بعدد الحالات + التفصيل.
/// قراءة فقط ضمن نطاق شركات المستخدم — المنطق بـ<see cref="PayrollTransactionsAuditor"/>.
/// </summary>
public class TransactionsAuditorModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public TransactionsAuditorModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public int? Month { get; set; }
    [BindProperty(SupportsGet = true)] public int? EmployeeId { get; set; }

    public List<PayrollTransactionsAuditor.EmployeeSummary> Summary { get; private set; } = new();
    public List<PayrollTransactionsAuditor.Finding> Findings { get; private set; } = new();
    public Dictionary<string, int> ByRule { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var all = await PayrollTransactionsAuditor.AuditAsync(_db, scope, Year, Month);

        Summary = PayrollTransactionsAuditor.Summarize(all);
        ByRule = all.GroupBy(f => f.Rule).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count());
        Findings = EmployeeId is > 0 ? all.Where(f => f.EmployeeId == EmployeeId).ToList() : all;
    }
}
