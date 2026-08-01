using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Forms;

/// <summary>
/// باني النماذج العام — الطبقة البنيوية الرابعة.
///
/// ⭐ يخدم **الطلبات المخصصة والاستبيانات معاً** لأن كيان يستعملهما ببانٍ واحد
/// (أثبته الفحص الحيّ). ونوع طلب أو استبيان جديد يصير **إعداداً بالواجهة** بدل
/// صفحة Razor + جدول + Store + معالج + نشر.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "edit")] public int? EditingId { get; set; }
    [BindProperty(SupportsGet = true, Name = "type")] public string? TypeFilter { get; set; }

    // القالب
    [BindProperty] public int TemplateId { get; set; }
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string? NameEn { get; set; }
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public string FormType { get; set; } = FormBuilder.FormTypeRequest;
    [BindProperty] public string Conditions { get; set; } = string.Empty;
    [BindProperty] public bool AllowEmployeeRequest { get; set; } = true;
    [BindProperty] public bool AllowManagerRequest { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;

    // مجموعة
    [BindProperty] public string? GroupName { get; set; }
    [BindProperty] public string? GroupNameEn { get; set; }

    // حقل
    [BindProperty] public int FieldId { get; set; }
    [BindProperty] public int? FieldGroupId { get; set; }
    [BindProperty] public string? FieldLabel { get; set; }
    [BindProperty] public string? FieldLabelEn { get; set; }
    [BindProperty] public string FieldControl { get; set; } = FormBuilder.ControlText;
    [BindProperty] public string? FieldOptions { get; set; }
    [BindProperty] public int? FieldScale { get; set; }
    [BindProperty] public bool FieldRequired { get; set; }

    public List<FormTemplateStore.Template> Templates { get; private set; } = new();
    public FormTemplateStore.Template? Current { get; private set; }
    public List<FormTemplateStore.Group> Groups { get; private set; } = new();
    public List<FormTemplateStore.Field> Fields { get; private set; } = new();
    public string CriteriaJson { get; private set; } = "[]";

    public IReadOnlyList<FormBuilder.ControlType> Controls =>
        FormBuilder.ControlsFor(Current?.FormType ?? FormType);

    /// <summary>الحقول مجمَّعةً للمعاينة: المجموعات بترتيبها ثم غير المجمَّعة.</summary>
    public List<(FormTemplateStore.Group? Group, List<FormTemplateStore.Field> Fields)> Layout()
    {
        var layout = Groups
            .Select(group => ((FormTemplateStore.Group?)group,
                Fields.Where(field => field.GroupId == group.Id).ToList()))
            .ToList();

        var ungrouped = Fields.Where(field => field.IsUngrouped).ToList();
        if (ungrouped.Count > 0)
        {
            layout.Add((null, ungrouped));
        }

        return layout;
    }

    public async Task OnGetAsync()
    {
        await LoadAsync();

        if (EditingId is > 0 && Current is not null)
        {
            TemplateId = Current.Id;
            Name = Current.Name;
            NameEn = Current.NameEn;
            Description = Current.Description;
            FormType = Current.FormType;
            Conditions = Current.ConditionsJson;
            AllowEmployeeRequest = Current.AllowEmployeeRequest;
            AllowManagerRequest = Current.AllowManagerRequest;
            IsActive = Current.IsActive;
        }
    }

    public async Task<IActionResult> OnPostSaveTemplateAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            TempData["ErrorMessage"] = "اسم النموذج إلزامي.";
            return RedirectToPage(new { edit = TemplateId > 0 ? TemplateId : (int?)null });
        }

        var id = await FormTemplateStore.SaveTemplateAsync(
            _db, TemplateId, Name.Trim(), NameEn, Description, FormType,
            HrConditions.Deserialize(Conditions), AllowEmployeeRequest, AllowManagerRequest,
            IsActive, User.Identity?.Name);

        TempData["SuccessMessage"] = "حُفظ النموذج.";
        return RedirectToPage(new { edit = id });
    }

    public async Task<IActionResult> OnPostDeleteTemplateAsync(int id)
    {
        await FormTemplateStore.DeleteTemplateAsync(_db, id);
        TempData["SuccessMessage"] = "حُذف النموذج.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDuplicateAsync(int id)
    {
        var newId = await FormTemplateStore.DuplicateAsync(_db, id, User.Identity?.Name);
        TempData["SuccessMessage"] = "أُنشئت نسخة معطّلة بمجموعاتها وحقولها — فعّلها بعد مراجعتها.";
        return RedirectToPage(new { edit = newId });
    }

    public async Task<IActionResult> OnPostAddGroupAsync(int templateId)
    {
        if (string.IsNullOrWhiteSpace(GroupName))
        {
            TempData["ErrorMessage"] = "اسم المجموعة إلزامي.";
            return RedirectToPage(new { edit = templateId });
        }

        var next = (await FormTemplateStore.LoadGroupsAsync(_db, templateId)).Count + 1;
        await FormTemplateStore.SaveGroupAsync(_db, 0, templateId, GroupName.Trim(), GroupNameEn, next);

        TempData["SuccessMessage"] = "أُضيفت المجموعة.";
        return RedirectToPage(new { edit = templateId });
    }

    public async Task<IActionResult> OnPostDeleteGroupAsync(int id, int templateId)
    {
        await FormTemplateStore.DeleteGroupAsync(_db, id);
        TempData["SuccessMessage"] = "حُذفت المجموعة — وحقولها بقيت غير مجمَّعة، لم تُحذف.";
        return RedirectToPage(new { edit = templateId });
    }

    public async Task<IActionResult> OnPostSaveFieldAsync(int templateId)
    {
        if (FormBuilder.ValidateField(FieldLabel, FieldControl, FieldOptions, FieldScale) is { } error)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToPage(new { edit = templateId });
        }

        var next = (await FormTemplateStore.LoadFieldsAsync(_db, templateId)).Count + 1;

        await FormTemplateStore.SaveFieldAsync(
            _db, FieldId, templateId, FieldGroupId, FieldLabel!.Trim(), FieldLabelEn,
            FieldControl, FieldOptions, FieldScale, FieldRequired,
            FieldId > 0 ? next : next);

        TempData["SuccessMessage"] = FieldId > 0 ? "حُدِّث الحقل." : "أُضيف الحقل.";
        return RedirectToPage(new { edit = templateId });
    }

    public async Task<IActionResult> OnPostDeleteFieldAsync(int id, int templateId)
    {
        await FormTemplateStore.DeleteFieldAsync(_db, id);
        TempData["SuccessMessage"] = "حُذف الحقل.";
        return RedirectToPage(new { edit = templateId });
    }

    public async Task<IActionResult> OnPostMoveFieldAsync(int id, int templateId, int direction)
    {
        await FormTemplateStore.MoveFieldAsync(_db, templateId, id, direction);
        return RedirectToPage(new { edit = templateId });
    }

    private async Task LoadAsync()
    {
        Templates = await FormTemplateStore.LoadTemplatesAsync(_db, TypeFilter);
        CriteriaJson = await HrConditionOptions.BuildCatalogJsonAsync(_db);

        if (EditingId is > 0)
        {
            Current = await FormTemplateStore.FindTemplateAsync(_db, EditingId.Value);

            if (Current is not null)
            {
                Groups = await FormTemplateStore.LoadGroupsAsync(_db, Current.Id);
                Fields = await FormTemplateStore.LoadFieldsAsync(_db, Current.Id);
            }
        }
    }
}
