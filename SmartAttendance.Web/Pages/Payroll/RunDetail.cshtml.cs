using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.Integrations;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// تفاصيل دفعة المسير (/Payroll/RunDetail?id=) — سطر لكل موظف بالإجمالي والاستقطاعات
/// والصافي، مع قسيمة تفصيلية (بنود الإضافات والاستقطاعات) لكل موظف.
/// </summary>
public class RunDetailModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public RunDetailModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    private readonly ICompanyScopeProvider _companyScope;

    /// <summary>
    /// بوابة الصفحة: تفاصيل الدفعة وقسائمها وملفها البنكيّ (بالآيبانات) كانت تُفتح
    /// بمعرّفٍ من الـquery بلا أي فحص ملكية — تعداد `?Id=` يقرأ رواتب كل الشركات.
    /// </summary>
    private async Task<bool> CanAccessAsync() =>
        await PayrollRunStore.CanAccessRunAsync(
            _db, Id, await _companyScope.GetAsync(HttpContext.RequestAborted));

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public PayrollRunStore.PayrollRun? Run { get; set; }
    public List<PayrollRunStore.PayrollLine> Lines { get; set; } = new();
    public string CompanyName { get; set; } = "الشركة";
    /// <summary>المستبعَدون من الدفعة بأسبابهم — يجيبون «لماذا لم يُحتسب فلان؟».</summary>
    public List<PayrollRunStore.Exclusion> Exclusions { get; set; } = new();

    public List<BankFileTemplateStore.Template> BankTemplates { get; set; } = new();

    public decimal TotalDeductions => Lines.Sum(l => l.TotalDeductions);
    public decimal TotalGosiEmployee => Lines.Sum(l => l.GosiEmployee);
    public decimal TotalOther => Lines.Sum(l => l.OtherDeductions);
    public decimal TotalEmployerCost => Lines.Sum(l => l.EmployerCost);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CanAccessAsync()) return RedirectToPage("Runs");

        Run = await PayrollRunStore.GetRunAsync(_db, Id);
        if (Run == null) return RedirectToPage("Runs");
        Lines = await PayrollRunStore.ListLinesAsync(_db, Id);
        Exclusions = await PayrollRunStore.ListExclusionsAsync(_db, Id);
        BankTemplates = await BankFileTemplateStore.ActiveAsync(
            _db, await _companyScope.GetAsync(HttpContext.RequestAborted), Run.CompanyId);
        CompanyName = Run.CompanyId is > 0
            ? await HrmsDatabase.ScalarAsync<string>(_db,
                "SELECT ISNULL(Name, N'الشركة') FROM Companies WHERE Id=@Id AND ISNULL(IsDeleted,0)=0;",
                command => HrmsDatabase.AddParameter(command, "@Id", Run.CompanyId.Value)) ?? "الشركة"
            : "الشركة";
        return Page();
    }

    /// <summary>
    /// ملف البنك للدفعة (CSV بترميز UTF-8 مع BOM ليفتحه إكسل عربياً سليماً).
    /// عمود «قابل للتحويل» يفضح الصفوف بلا آيبان/بطاقة بدل إسقاطها بصمت،
    /// فلا يُرسَل للبنك ملف ناقص دون أن يعلم أحد.
    /// </summary>
    public async Task<IActionResult> OnGetBankFileAsync(int? templateId)
    {
        if (!await CanAccessAsync()) return RedirectToPage("Runs");

        var run = await PayrollRunStore.GetRunAsync(_db, Id);
        if (run == null) return RedirectToPage("Runs");
        if (run.Status is not ("Issued" or "PayslipSent"))
        {
            TempData["PayrollMessage"] = "ملف البنك متاح بعد اعتماد الدفعة للصرف فقط.";
            TempData["PayrollOk"] = false;
            return RedirectToPage("RunDetail", new { id = Id });
        }

        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var template = templateId is > 0
            ? await BankFileTemplateStore.GetAsync(_db, scope, templateId.Value, run.CompanyId)
            : await BankFileTemplateStore.DefaultAsync(_db, scope, run.CompanyId);
        template ??= await BankFileTemplateStore.DefaultAsync(_db, scope, run.CompanyId);
        if (template == null) return RedirectToPage("Runs");

        var rows = await PayrollRunStore.BankFileRowsAsync(_db, Id);
        var content = BankFileTemplateStore.BuildContent(template, rows);

        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(content))
            .ToArray();

        var safeName = template.Name.Replace(' ', '-');
        return File(bytes, "text/csv", $"BankFile-{safeName}-{run.BatchNo}.csv");
    }

    public async Task<IActionResult> OnGetExportAccountingJournalAsync(string format = "csv")
    {
        if (!await CanAccessAsync()) return RedirectToPage("Runs");
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        AccountingJournalAdapter.Journal? journal;
        try
        {
            journal = await AccountingJournalStore.BuildForRunAsync(_db, scope, Id);
        }
        catch (InvalidOperationException exception)
        {
            TempData["PayrollMessage"] = exception.Message;
            TempData["PayrollOk"] = false;
            return RedirectToPage("RunDetail", new { id = Id });
        }
        if (journal is null)
        {
            TempData["PayrollMessage"] = "القيد المحاسبي متاح لمسير صادر فقط وضمن شركتك.";
            TempData["PayrollOk"] = false;
            return RedirectToPage("RunDetail", new { id = Id });
        }
        var run = await PayrollRunStore.GetRunAsync(_db, Id);
        if (run?.CompanyId is not > 0) return Forbid();
        var useJson = format.Equals("json", StringComparison.OrdinalIgnoreCase);
        var bytes = useJson ? AccountingJournalAdapter.Json(journal) : AccountingJournalAdapter.Csv(journal);
        var exportFormat = useJson ? "json" : "csv";
        await using var transaction = await _db.Database.BeginTransactionAsync(HttpContext.RequestAborted);
        await AccountingJournalStore.RecordExportAsync(
            _db, scope, run.CompanyId.Value, run.Id, exportFormat, bytes,
            User.Identity?.Name ?? "system");
        await WebhookStore.EnqueueAsync(_db, run.CompanyId.Value, "accounting.journal.exported",
            new
            {
                eventType = "accounting.journal.exported", runId = run.Id, run.BatchNo,
                format = exportFormat, journal.TotalDebit, journal.TotalCredit,
                occurredAt = DateTimeOffset.UtcNow
            }, $"accounting.journal.exported:{run.Id}:{exportFormat}:{Guid.NewGuid():N}");
        await transaction.CommitAsync(HttpContext.RequestAborted);
        return File(bytes, useJson ? "application/json" : "text/csv; charset=utf-8",
            $"Journal-{run.BatchNo}.{exportFormat}");
    }
}
