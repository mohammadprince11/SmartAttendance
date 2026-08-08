using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Acknowledgments;

/// <summary>
/// قوالب الإقرارات (نظير «قالب الإقرارات» بكيان) — الإقرار وثيقة مكتملة بذاتها
/// (عنوان + نصّ + جمهور)، ولا تعتمد على منشئ الوثائق.
///
/// جمهور الإرسال يُحدَّد بمحرّك الشروط العام نفسه — فلا حاجة لآلية استهداف خاصة
/// بالإقرارات، وهو المكسب المباشر من الطبقة البنيوية الأولى.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    private readonly SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider _companyScope;

    public IndexModel(ApplicationDbContext db, SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true, Name = "edit")] public int? EditingId { get; set; }

    [BindProperty] public int TemplateId { get; set; }
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public string? Title { get; set; }
    [BindProperty] public string? Body { get; set; }
    [BindProperty] public bool SendToNewHires { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public string Conditions { get; set; } = string.Empty;

    public List<AcknowledgmentStore.Template> Templates { get; private set; } = new();
    public Dictionary<int, (int Sent, int Accepted, int Pending)> Stats { get; private set; } = new();
    public string CriteriaJson { get; private set; } = "[]";

    public async Task OnGetAsync()
    {
        await LoadAsync();

        if (EditingId is > 0 && await AcknowledgmentStore.FindTemplateAsync(_db, EditingId.Value) is { } template)
        {
            TemplateId = template.Id;
            Name = template.Name;
            Description = template.Description;
            Title = template.Title;
            Body = template.Body;
            SendToNewHires = template.SendToNewHires;
            IsActive = template.IsActive;
            Conditions = template.ConditionsJson;
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            TempData["ErrorMessage"] = "اسم القالب إلزامي.";
            return RedirectToPage();
        }

        await AcknowledgmentStore.SaveTemplateAsync(
            _db, TemplateId, Name.Trim(), Description, Title, Body,
            SendToNewHires, IsActive, HrConditions.Deserialize(Conditions), User.Identity?.Name);

        TempData["SuccessMessage"] = TemplateId > 0 ? "تم تحديث القالب." : "تم إنشاء القالب.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await AcknowledgmentStore.DeleteTemplateAsync(_db, id);
        TempData["SuccessMessage"] = "تم حذف القالب — الإرسالات السابقة تبقى كإثبات.";
        return RedirectToPage();
    }

    /// <summary>الإرسال للجمهور الذي تحدّده شروط القالب.</summary>
    public async Task<IActionResult> OnPostSendAsync(int id)
    {
        var template = await AcknowledgmentStore.FindTemplateAsync(_db, id);
        if (template is null)
        {
            TempData["ErrorMessage"] = "القالب غير موجود.";
            return RedirectToPage();
        }

        var audience = await AcknowledgmentStore.ResolveAudienceAsync(
            _db, await _companyScope.GetAsync(), template.Conditions, DateOnly.FromDateTime(DateTime.Today));

        var sent = await AcknowledgmentStore.SendAsync(_db, id, audience, User.Identity?.Name);

        TempData["SuccessMessage"] = sent == 0
            ? "لم يُرسل لأحد — شروط القالب لا تطابق أي موظف نشط."
            : $"أُرسل الإقرار إلى {sent} موظفاً. تابع المشاهدة والموافقة بصفحة المتابعة.";

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Templates = await AcknowledgmentStore.LoadTemplatesAsync(_db);
        CriteriaJson = await HrConditionOptions.BuildCatalogJsonAsync(_db);

        var assignments = await AcknowledgmentStore.LoadAssignmentsAsync(_db, await _companyScope.GetAsync());
        Stats = assignments
            .GroupBy(row => row.TemplateId)
            .ToDictionary(
                group => group.Key,
                group => (
                    Sent: group.Count(),
                    Accepted: group.Count(row => row.Accepted),
                    Pending: group.Count(row => row.NeedsAction)));
    }
}
