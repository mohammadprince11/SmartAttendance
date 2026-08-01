using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Documents;

/// <summary>
/// قوالب الوثائق (منشئ الوثائق — م١). نظير `/Setup/DocumentBuilder` بكيان.
///
/// الفجوة التي يسدّها: شهادات الراتب وكتب التعريف وخطابات التجربة كانت تُكتب
/// بـWord **خارج النظام** — بلا مرجع ولا أرشيف ولا بيانات محدَّثة.
/// </summary>
public class TemplatesModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public TemplatesModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "edit")] public int? EditingId { get; set; }

    [BindProperty] public int TemplateId { get; set; }
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string? NameEn { get; set; }
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public string? Body { get; set; }
    [BindProperty] public string? RefPrefix { get; set; } = "DOC";
    [BindProperty] public bool AllowEmployeeRequest { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public string Conditions { get; set; } = string.Empty;

    public List<DocumentTemplateStore.Template> Templates { get; private set; } = new();
    public IReadOnlyList<DocumentTokenEngine.Token> Tokens { get; private set; } = DocumentTokenEngine.Catalog;
    public string CriteriaJson { get; private set; } = "[]";
    public List<string> UnknownTokens { get; private set; } = new();
    public Dictionary<int, int> GeneratedCounts { get; private set; } = new();

    public async Task OnGetAsync()
    {
        await LoadAsync();

        if (EditingId is > 0 && await DocumentTemplateStore.FindTemplateAsync(_db, EditingId.Value) is { } template)
        {
            TemplateId = template.Id;
            Name = template.Name;
            NameEn = template.NameEn;
            Description = template.Description;
            Body = template.Body;
            RefPrefix = template.RefPrefix;
            AllowEmployeeRequest = template.AllowEmployeeRequest;
            IsActive = template.IsActive;
            Conditions = template.ConditionsJson;
            UnknownTokens = DocumentTokenEngine.UnknownTokens(template.Body, Tokens);
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            TempData["ErrorMessage"] = "اسم القالب إلزامي.";
            return RedirectToPage();
        }

        var id = await DocumentTemplateStore.SaveTemplateAsync(
            _db, TemplateId, Name.Trim(), NameEn, Description, Body,
            HrConditions.Deserialize(Conditions), RefPrefix, AllowEmployeeRequest, IsActive, User.Identity?.Name);

        // الرمز المخطئ إملائياً يُحفظ ويبقى ظاهراً بالوثيقة — التنبيه هنا يمنع
        // اكتشافه بعد إصدار مئة شهادة.
        await LoadAsync();
        var unknown = DocumentTokenEngine.UnknownTokens(Body, Tokens);

        TempData["SuccessMessage"] = unknown.Count == 0
            ? "حُفظ القالب (ونُقّي نصّه من أي وسوم غير آمنة)."
            : $"حُفظ القالب — لكن فيه {unknown.Count} رمزاً غير معروف سيظهر كما هو بالوثيقة: {string.Join("، ", unknown)}";

        return RedirectToPage(new { edit = id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await DocumentTemplateStore.DeleteTemplateAsync(_db, id);
        TempData["SuccessMessage"] = "حُذف القالب — والوثائق الصادرة منه تبقى بأرشيفها.";
        return RedirectToPage();
    }

    /// <summary>«انشئ نسخة» — نمط كيان المتكرّر بباقي الشاشات.</summary>
    public async Task<IActionResult> OnPostDuplicateAsync(int id)
    {
        if (await DocumentTemplateStore.FindTemplateAsync(_db, id) is { } source)
        {
            var newId = await DocumentTemplateStore.SaveTemplateAsync(
                _db, 0, $"{source.Name} (نسخة)", source.NameEn, source.Description, source.Body,
                source.Conditions, source.RefPrefix, source.AllowEmployeeRequest, false, User.Identity?.Name);

            TempData["SuccessMessage"] = "أُنشئت نسخة معطّلة — فعّلها بعد مراجعتها.";
            return RedirectToPage(new { edit = newId });
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Templates = await DocumentTemplateStore.LoadTemplatesAsync(_db);
        CriteriaJson = await HrConditionOptions.BuildCatalogJsonAsync(_db);

        var customFields = await HrConditionFacts.LoadCustomFieldDefinitionsAsync(_db);
        Tokens = DocumentTokenEngine.WithCustomFields(customFields);

        var generated = await DocumentTemplateStore.LoadGeneratedAsync(_db);
        GeneratedCounts = generated
            .GroupBy(row => row.TemplateId)
            .ToDictionary(group => group.Key, group => group.Count());
    }
}
