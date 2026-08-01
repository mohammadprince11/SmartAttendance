using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

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

    public TerminationSettlementModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public int? EmployeeId { get; set; }
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }

    /// <summary>المستحقّ يُدخَل يدوياً هذه المرحلة — لا يُخمَّن.</summary>
    [BindProperty] public decimal? DueTax { get; set; }
    [BindProperty] public decimal? DueGosi { get; set; }

    public List<(int Id, string Label)> Employees { get; private set; } = new();
    public TerminationSettlementStore.YearWithholding? Withheld { get; private set; }
    public List<TerminationSettlementPolicy.Difference> Differences { get; private set; } = new();
    public decimal Net { get; private set; }
    public string NetInWords { get; private set; } = string.Empty;

    public DateOnly? ServiceEndDate { get; private set; }
    public DateOnly? HireDate { get; private set; }
    public string ServiceLength { get; private set; } = string.Empty;
    public bool MonthUnpaidWarning { get; private set; }
    public string? EmployeeName { get; private set; }

    public int SelectedYear => Year ?? DateTime.Today.Year;

    public async Task OnGetAsync()
    {
        await LoadEmployeesAsync();

        if (EmployeeId is > 0)
        {
            await LoadEmployeeAsync(EmployeeId.Value);
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadEmployeesAsync();

        if (EmployeeId is > 0)
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
SELECT ISNULL(FullName, N'') AS FullName, HireDate, JoiningDate, ServiceEndDate
FROM Employees WHERE Id = @Id;
""",
            command => HrmsDatabase.AddParameter(command, "@Id", employeeId),
            reader => (
                Name: HrmsDatabase.GetString(reader, "FullName"),
                Hire: HrmsDatabase.GetDateOnly(reader, "HireDate"),
                Joining: HrmsDatabase.GetDateOnly(reader, "JoiningDate"),
                End: HrmsDatabase.GetDateOnly(reader, "ServiceEndDate")));

        if (rows.FirstOrDefault() is var row && row.Name is not null)
        {
            EmployeeName = row.Name;
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

    private async Task LoadEmployeesAsync() =>
        Employees = (await HrmsDatabase.QueryAsync(
            _db,
            """
SELECT Id, ISNULL(FullName, N'') AS FullName, ISNULL(EmployeeNo, N'') AS EmployeeNo, ServiceEndDate
FROM Employees
WHERE ISNULL(IsDeleted, 0) = 0
ORDER BY CASE WHEN ServiceEndDate IS NULL THEN 1 ELSE 0 END, FullName;
""",
            command => { },
            reader => (
                HrmsDatabase.GetInt(reader, "Id"),
                $"{HrmsDatabase.GetString(reader, "FullName")} ({HrmsDatabase.GetString(reader, "EmployeeNo")})"
                + (HrmsDatabase.GetDateOnly(reader, "ServiceEndDate") is { } end ? $" — أُنهيت {end:yyyy/MM/dd}" : ""))))
            .ToList();
}
