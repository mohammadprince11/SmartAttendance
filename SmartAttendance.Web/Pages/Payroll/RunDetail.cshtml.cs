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
    public List<BankFileTemplateStore.Template> BankTemplates { get; set; } = new();

    public decimal TotalDeductions => Lines.Sum(l => l.TotalDeductions);
    public decimal TotalGosiEmployee => Lines.Sum(l => l.GosiEmployee);
    public decimal TotalOther => Lines.Sum(l => l.OtherDeductions);
    public decimal TotalEmployerCost => Lines.Sum(l => l.EmployerCost);

    public async Task<IActionResult> OnGetAsync()
    {
        Run = await PayrollRunStore.GetRunAsync(_db, Id);
        if (Run == null) return RedirectToPage("Runs");
        Lines = await PayrollRunStore.ListLinesAsync(_db, Id);
        BankTemplates = await BankFileTemplateStore.ActiveAsync(_db);
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
    public async Task<IActionResult> OnGetBankFileAsync(int? templateId)
    {
        var run = await PayrollRunStore.GetRunAsync(_db, Id);
        if (run == null) return RedirectToPage("Runs");

        var template = templateId is > 0
            ? await BankFileTemplateStore.GetAsync(_db, templateId.Value)
            : await BankFileTemplateStore.DefaultAsync(_db);
        template ??= await BankFileTemplateStore.DefaultAsync(_db);
        if (template == null) return RedirectToPage("Runs");

        var rows = await PayrollRunStore.BankFileRowsAsync(_db, Id);
        var content = BankFileTemplateStore.BuildContent(template, rows);

        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(content))
            .ToArray();

        var safeName = template.Name.Replace(' ', '-');
        return File(bytes, "text/csv", $"BankFile-{safeName}-{run.BatchNo}.csv");
    }
}
