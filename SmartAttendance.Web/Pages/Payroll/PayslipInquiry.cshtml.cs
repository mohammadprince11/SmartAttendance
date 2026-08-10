using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// استعلام قسائم الراتب (/Payroll/PayslipInquiry) — نظير «استعلام قسائم الراتب» بكيان:
/// ابحث عن موظف (بالمنتقي المشترك) + الفترة/السنة، فتظهر قسائمه عبر كل الدورات مع رابط
/// لعرض القسيمة الكاملة لذلك الموظف. قراءةٌ فقط، مقيَّدة بنطاق شركات المستخدم.
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

    [BindProperty(SupportsGet = true)]
    public int? EmployeeId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    public List<PayrollRunStore.PayslipSummary> Payslips { get; set; } = new();
    public string SelectedEmployeeCode { get; set; } = string.Empty;
    public string SelectedEmployeeName { get; set; } = string.Empty;

    public async Task OnGetAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (EmployeeId is not > 0 || scope.IsDeniedAll) return;

        // اسم/رمز الموظف المختار — لعرضه بالمنتقي، مقيَّداً بنطاق شركات المستخدم (لا تسريب).
        var empPredicate = scope.IsUnrestricted ? "1=1" : scope.ToSqlPredicate("CompanyId");
        var emp = (await HrmsDatabase.QueryAsync(_db,
            $"SELECT TOP 1 ISNULL(EmployeeNo, N'') AS Code, ISNULL(FullName, N'') AS Name FROM Employees WHERE Id = @Id AND ({empPredicate}) AND ISNULL(IsDeleted,0)=0;",
            command => HrmsDatabase.AddParameter(command, "@Id", EmployeeId.Value),
            reader => new { Code = HrmsDatabase.GetString(reader, "Code"), Name = HrmsDatabase.GetString(reader, "Name") }))
            .FirstOrDefault();

        if (emp == null) { EmployeeId = null; return; } // خارج النطاق

        SelectedEmployeeCode = emp.Code;
        SelectedEmployeeName = emp.Name;
        Payslips = await PayrollRunStore.PayslipHistoryAsync(_db, scope, EmployeeId.Value, Year, Month);
    }
}
