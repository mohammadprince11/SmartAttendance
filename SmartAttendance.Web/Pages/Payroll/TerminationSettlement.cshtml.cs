using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// تسوية الإنهاء — بنود «الفروقات» (أخطر فجوة مالية بالجرد).
///
/// ⚠️⚠️ <b>هذه الشاشة استشارية بالكامل: لا تكتب بالقاعدة ولا تغيّر مبلغ نهاية
/// خدمة.</b> تعرض ما اقتُطع فعلاً مقابل ما كان مستحقّاً، والقرار بيد المستخدم.
///
/// السبب: تغيير صيغة مالية قائمة ممنوع بلا طلب صريح لهذه الصيغة بعينها
/// (<c>CLAUDE.md</c>)، وحسابُ «المستحقّ» يعتمد على ملف ضريبة الموظف الذي لم يُبنَ
/// بعد. فالعرض اليوم يمنع الخطأ، والتطبيق التلقائي ينتظر الملفات المالية.
/// </summary>
public class TerminationSettlementModel : PageModel
{
    private readonly ApplicationDbContext _db;

    private readonly ICompanyScopeProvider _companyScope;

    public TerminationSettlementModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    /// <summary>
    /// حارس الملكية. الشاشة تكشف الضريبة والضمان المحتجزين سنوياً لأي موظف بمعرّفٍ
    /// من الـquery — بلا أي فحص قبل هذا الإصلاح.
    /// </summary>
    private async Task<bool> CanAccessAsync(int employeeId) =>
        await EmployeeCompanyGuard.CanAccessEmployeeAsync(
            _db, employeeId, await _companyScope.GetAsync(HttpContext.RequestAborted),
            HttpContext.RequestAborted);

    [BindProperty(SupportsGet = true)] public int? EmployeeId { get; set; }
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }

    /// <summary>المستحقّ يُدخَل يدوياً هذه المرحلة — لا يُخمَّن.</summary>
    [BindProperty] public decimal? DueTax { get; set; }
    [BindProperty] public decimal? DueGosi { get; set; }

    public TerminationSettlementStore.YearWithholding? Withheld { get; private set; }
    public List<TerminationSettlementPolicy.Difference> Differences { get; private set; } = new();
    public decimal Net { get; private set; }
    public string NetInWords { get; private set; } = string.Empty;

    public DateOnly? ServiceEndDate { get; private set; }
    public DateOnly? HireDate { get; private set; }
    public string ServiceLength { get; private set; } = string.Empty;
    public bool MonthUnpaidWarning { get; private set; }
    public string? EmployeeName { get; private set; }

    /// <summary>رمز الموظف المحسوم — لتعبئة المنتقي ابتداءً.</summary>
    public string? EmployeeCode { get; private set; }

    public int SelectedYear => Year ?? DateTime.Today.Year;

    public async Task OnGetAsync()
    {
        if (EmployeeId is > 0 && await CanAccessAsync(EmployeeId.Value))
        {
            await LoadEmployeeAsync(EmployeeId.Value);
        }
        else
        {
            // خارج النطاق ⟹ الشاشة فارغة كما لو لم يُمرَّر موظف. لا رسالة تؤكّد وجوده.
            EmployeeId = null;
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (EmployeeId is > 0 && await CanAccessAsync(EmployeeId.Value))
        {
            await LoadEmployeeAsync(EmployeeId.Value);
            BuildDifferences();
        }

        return Page();
    }

    private void BuildDifferences()
    {
        if (Withheld is null)
        {
            return;
        }

        // بندٌ يُترك مستحقّه فارغاً يُستبعَد كلياً — صفرٌ بمكانه كان سيعني
        // «المستحقّ صفر» فيُظهر فرقاً كامل المبلغ ويُوهم بردٍّ لا أساس له.
        var items = new List<TerminationSettlementPolicy.Difference>();

        if (DueTax is { } tax)
        {
            items.Add(new TerminationSettlementPolicy.Difference(
                TerminationSettlementPolicy.ItemTax, Withheld.Tax, tax));
        }

        if (DueGosi is { } gosi)
        {
            items.Add(new TerminationSettlementPolicy.Difference(
                TerminationSettlementPolicy.ItemGosi, Withheld.Gosi, gosi));
        }

        Differences = TerminationSettlementPolicy.Material(items);
        Net = TerminationSettlementPolicy.NetDifference(items);
        NetInWords = TerminationSettlementPolicy.AmountInWords(Net);
    }

    private async Task LoadEmployeeAsync(int employeeId)
    {
        Withheld = await TerminationSettlementStore.LoadYearAsync(_db, employeeId, SelectedYear);

        var rows = await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT ISNULL(FullName, N'') AS FullName, ISNULL(EmployeeNo, N'') AS EmployeeNo,
       HireDate, JoiningDate, ServiceEndDate
FROM Employees WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", employeeId),
            reader => (
                Name: HrmsDatabase.GetString(reader, "FullName"),
                Code: HrmsDatabase.GetString(reader, "EmployeeNo"),
                Hire: HrmsDatabase.GetDateOnly(reader, "HireDate"),
                Joining: HrmsDatabase.GetDateOnly(reader, "JoiningDate"),
                End: HrmsDatabase.GetDateOnly(reader, "ServiceEndDate")));

        if (rows.FirstOrDefault() is var row && row.Name is not null)
        {
            EmployeeName = row.Name;
            EmployeeCode = row.Code;
            HireDate = row.Joining ?? row.Hire;
            ServiceEndDate = row.End;

            if (HireDate is { } from && ServiceEndDate is { } to)
            {
                ServiceLength = TerminationSettlementPolicy.ServiceLength(from, to);
            }

            if (ServiceEndDate is { } termination)
            {
                var lastPaid = await TerminationSettlementStore.LastPaidMonthKeyAsync(_db, employeeId);
                MonthUnpaidWarning = TerminationSettlementPolicy.TerminationMonthUnpaid(termination, lastPaid);
            }
        }
    }

}
