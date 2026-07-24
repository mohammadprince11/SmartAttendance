using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// تفاصيل دفعة المسير (/Payroll/RunDetail?id=) — سطر لكل موظف بالإجمالي والاستقطاعات
/// والصافي، مع قسيمة تفصيلية (بنود الإضافات والاستقطاعات) لكل موظف.
/// </summary>
public class RunDetailModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public RunDetailModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public PayrollRunStore.PayrollRun? Run { get; set; }
    public List<PayrollRunStore.PayrollLine> Lines { get; set; } = new();
    public string CompanyName { get; set; } = "الشركة";

    public decimal TotalDeductions => Lines.Sum(l => l.TotalDeductions);
    public decimal TotalGosiEmployee => Lines.Sum(l => l.GosiEmployee);
    public decimal TotalOther => Lines.Sum(l => l.OtherDeductions);
    public decimal TotalEmployerCost => Lines.Sum(l => l.EmployerCost);

    public async Task<IActionResult> OnGetAsync()
    {
        Run = await PayrollRunStore.GetRunAsync(_db, Id);
        if (Run == null) return RedirectToPage("Runs");
        Lines = await PayrollRunStore.ListLinesAsync(_db, Id);
        CompanyName = await HrmsDatabase.ScalarAsync<string>(_db,
            "SELECT TOP 1 ISNULL(Name, N'الشركة') FROM Companies WHERE ISNULL(IsDeleted,0)=0 ORDER BY Id;")
            ?? "الشركة";
        return Page();
    }

    /// <summary>
    /// ملف البنك للدفعة (CSV بترميز UTF-8 مع BOM ليفتحه إكسل عربياً سليماً).
    /// عمود «قابل للتحويل» يفضح الصفوف بلا آيبان/بطاقة بدل إسقاطها بصمت،
    /// فلا يُرسَل للبنك ملف ناقص دون أن يعلم أحد.
    /// </summary>
    public async Task<IActionResult> OnGetBankFileAsync()
    {
        var run = await PayrollRunStore.GetRunAsync(_db, Id);
        if (run == null) return RedirectToPage("Runs");

        var rows = await PayrollRunStore.BankFileRowsAsync(_db, Id);

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("رقم الموظف,الاسم,طريقة الدفع,البنك,الفرع,الآيبان,رقم البطاقة,صافي الراتب,قابل للتحويل");

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',',
                Csv(row.EmployeeNo), Csv(row.EmployeeName), Csv(row.PaymentMethod),
                Csv(row.BankName), Csv(row.BankBranch), Csv(row.Iban), Csv(row.CardNo),
                row.NetSalary.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                row.IsPayable ? "نعم" : "لا — بلا آيبان/بطاقة"));
        }

        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(builder.ToString()))
            .ToArray();

        return File(bytes, "text/csv", $"BankFile-{run.BatchNo}.csv");
    }

    /// <summary>تهريب حقل CSV: يُقتبس عند وجود فاصلة/اقتباس/سطر جديد.</summary>
    private static string Csv(string? value)
    {
        var text = value ?? string.Empty;
        return text.Any(c => c is ',' or '"' or '\n' or '\r')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }
}
