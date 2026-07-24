using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Payroll;

/// <summary>
/// قوالب ملفات البنوك (/Payroll/BankTemplates) — إدارة تنسيقات تصدير المسير للبنك:
/// الأعمدة/ترتيبها/رؤوسها والفاصل والترويسة والقالب الافتراضي.
/// </summary>
public class BankTemplatesModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public BankTemplatesModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<BankFileTemplateStore.Template> Templates { get; set; } = new();
    public IReadOnlyList<(string Key, string Label)> Fields => BankFileTemplateStore.Fields;
    public IReadOnlyList<(string Key, string Label, string Char)> Delimiters => BankFileTemplateStore.Delimiters;

    public async Task OnGetAsync()
    {
        Templates = await BankFileTemplateStore.ListAsync(_db);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var f = Request.Form;
        var columns = f["Columns"].Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()).ToList();
        // الرؤوس المخصصة بترتيب الأعمدة المختارة (مجموعة بـ| من الواجهة، محاذية لـColumns).
        var headersJoined = f["HeadersJoined"].ToString();
        var hasCustomHeaders = headersJoined.Replace("|", "").Trim().Length > 0;

        var tpl = new BankFileTemplateStore.Template
        {
            Id = int.TryParse(f["Id"], out var id) ? id : 0,
            Name = f["Name"].ToString().Trim(),
            BankName = string.IsNullOrWhiteSpace(f["BankName"]) ? null : f["BankName"].ToString().Trim(),
            Delimiter = f["Delimiter"].ToString() is { Length: > 0 } d ? d : "Comma",
            IncludeHeader = f["IncludeHeader"] == "true" || f["IncludeHeader"] == "on",
            ColumnsCsv = string.Join(",", columns),
            HeadersCsv = hasCustomHeaders ? headersJoined : null,
            IsDefault = f["IsDefault"] == "true" || f["IsDefault"] == "on",
            IsActive = f["IsActive"] == "true" || f["IsActive"] == "on"
        };

        var (ok, message) = await BankFileTemplateStore.SaveAsync(_db, tpl);
        TempData["BtMessage"] = message;
        TempData["BtOk"] = ok;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await BankFileTemplateStore.DeleteAsync(_db, id);
        TempData["BtMessage"] = "حُذف القالب.";
        TempData["BtOk"] = true;
        return RedirectToPage();
    }
}
