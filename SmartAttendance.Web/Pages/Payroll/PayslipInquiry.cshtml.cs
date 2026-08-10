using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// استعلام القسائم (/Payroll/PayslipInquiry) — نظير «استعلام القسائم» بكيان: اختر موظفاً
/// فتظهر قسائمه عبر كل الدورات (الفترة/الحالة/الإجمالي/الصافي) مع رابط لعرض القسيمة الكاملة.
/// قراءةٌ فقط، مقيَّدة بنطاق شركات المستخدم (لا تسريب عبر الشركات).
/// </summary>
public class PayslipInquiryModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public PayslipInquiryModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    public sealed class CompanyOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class EmployeeOption
    {
        public int Id { get; set; }
        public string No { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public List<CompanyOption> Companies { get; set; } = new();
    public List<EmployeeOption> Employees { get; set; } = new();
    public List<int> AvailableYears { get; set; } = new();
    public List<PayrollRunStore.PayslipSummary> Payslips { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? EmployeeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    public string SelectedEmployeeName { get; set; } = string.Empty;

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

        // موظفو الشركة المختارة (لمنتقي الموظف). بلا شركة مختارة ⟹ لا قائمة (اختر شركة أولاً).
        if (CompanyId is > 0)
        {
            Employees = await HrmsDatabase.QueryAsync(_db,
                "SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName FROM Employees WHERE ISNULL(IsDeleted,0)=0 AND ISNULL(IsActive,1)=1 AND CompanyId=@Company ORDER BY EmployeeNo;",
                command => HrmsDatabase.AddParameter(command, "@Company", CompanyId.Value),
                reader => new EmployeeOption
                {
                    Id = HrmsDatabase.GetInt(reader, "Id"),
                    No = HrmsDatabase.GetString(reader, "EmployeeNo"),
                    Name = HrmsDatabase.GetString(reader, "FullName")
                });
        }

        // إن اختير موظف خارج الشركة/النطاق ⟹ تجاهُل.
        if (EmployeeId is > 0 && CompanyId is > 0 && !Employees.Any(e => e.Id == EmployeeId.Value))
            EmployeeId = null;

        if (EmployeeId is > 0)
        {
            Payslips = await PayrollRunStore.PayslipHistoryAsync(_db, scope, EmployeeId.Value, Year, Month);
            AvailableYears = Payslips.Select(p => p.Year).Distinct().OrderByDescending(y => y).ToList();
            SelectedEmployeeName = Employees.FirstOrDefault(e => e.Id == EmployeeId.Value) is { } emp
                ? $"{emp.No} — {emp.Name}"
                : string.Empty;
        }
    }
}
