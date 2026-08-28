using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// مستند طباعة مستقل لقسيمة موظف ضمن دفعة رواتب. لا يثق بمعرّفي الدفعة والموظف؛
/// يثبت أولاً أن الدفعة ضمن نطاق المستخدم ثم يختار الموظف من أسطرها فقط.
/// </summary>
public sealed class PayslipPrintModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public PayslipPrintModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public int RunId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int EmployeeId { get; set; }

    public PayrollRunStore.PayrollRun Run { get; private set; } = null!;
    public PayrollRunStore.PayrollLine Line { get; private set; } = null!;
    public string CompanyName { get; private set; } = "الشركة";
    public string PeriodRange { get; private set; } = string.Empty;
    public string NetInWords { get; private set; } = string.Empty;
    public int PaidDays { get; private set; }
    public int DaysBasis { get; private set; }
    public List<PrintRow> IncomeRows { get; private set; } = new();
    public List<PrintRow> DeductionRows { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (RunId <= 0 || EmployeeId <= 0) return NotFound();

        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!await PayrollRunStore.CanAccessRunAsync(_db, RunId, scope))
            return RedirectToPage("Runs");

        var run = await PayrollRunStore.GetRunAsync(_db, RunId);
        if (run == null) return NotFound();

        var lines = await PayrollRunStore.ListLinesAsync(_db, RunId);
        var line = lines.FirstOrDefault(line => line.EmployeeId == EmployeeId);
        if (line == null) return NotFound();

        Run = run;
        Line = line;
        CompanyName = run.CompanyId is > 0
            ? await HrmsDatabase.ScalarAsync<string>(_db,
                "SELECT ISNULL(Name, N'الشركة') FROM Companies WHERE Id=@Id AND ISNULL(IsDeleted,0)=0;",
                command => HrmsDatabase.AddParameter(command, "@Id", run.CompanyId.Value)) ?? "الشركة"
            : "الشركة";

        var periodStart = new DateOnly(run.Year, run.Month, 1);
        PeriodRange = $"{periodStart:yyyy/MM/dd} - {periodStart.AddMonths(1).AddDays(-1):yyyy/MM/dd}";
        NetInWords = TerminationSettlementPolicy.CurrencyAmountInWords(line.NetSalary, line.PayrollCurrency);
        DaysBasis = line.DaysBasis > 0 ? line.DaysBasis : line.WorkDays;
        PaidDays = DaysBasis > 0
            ? (int)Math.Round(DaysBasis * (line.AttendanceFactor == 0 ? 1 : line.AttendanceFactor),
                MidpointRounding.AwayFromZero)
            : 0;
        BuildLedger();
        return Page();
    }

    public string PaymentMethodLabel => (Line.PaymentMethod ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "bank" => "تحويل بنكي",
        "cash" => "نقدي",
        "cheque" or "check" => "شيك",
        { Length: > 0 } value => value,
        _ => "—"
    };

    private void BuildLedger()
    {
        IncomeRows = Line.Earnings
            .Select(component => new PrintRow(component.ItemName, component.Amount,
                component.Kind.Equals("Basic", StringComparison.OrdinalIgnoreCase) ? PaidDays : null,
                component.Kind))
            .ToList();
        DeductionRows = Line.Deductions
            .Select(component => new PrintRow(component.ItemName, component.Amount, null, component.Kind))
            .ToList();

        if (Line.BasicSalary != 0 && !IncomeRows.Any(row => row.Kind.Equals("Basic", StringComparison.OrdinalIgnoreCase)))
            IncomeRows.Insert(0, new PrintRow("الراتب المستحق", Line.BasicSalary, PaidDays, "Basic"));

        var incomeGap = Line.GrossSalary - IncomeRows.Sum(row => row.Amount);
        if (Math.Abs(incomeGap) >= 0.01m)
            IncomeRows.Add(new PrintRow("تسوية الاستحقاقات", incomeGap, null, "Adjustment"));

        if (Line.TaxAmount != 0 && !DeductionRows.Any(row => row.Kind.Equals("Tax", StringComparison.OrdinalIgnoreCase)))
            DeductionRows.Add(new PrintRow("ضريبة الدخل", Line.TaxAmount, null, "Tax"));
        if (Line.GosiEmployee != 0 && !DeductionRows.Any(row => row.Kind.Equals("Gosi", StringComparison.OrdinalIgnoreCase)))
            DeductionRows.Add(new PrintRow("الضمان الاجتماعي (حصة الموظف)", Line.GosiEmployee, null, "Gosi"));

        var deductionGap = Line.TotalDeductions - DeductionRows.Sum(row => row.Amount);
        if (Math.Abs(deductionGap) >= 0.01m)
            DeductionRows.Add(new PrintRow("خصومات أخرى", deductionGap, null, "Other"));
    }

    public sealed record PrintRow(string Name, decimal Amount, int? Days, string Kind);
}
