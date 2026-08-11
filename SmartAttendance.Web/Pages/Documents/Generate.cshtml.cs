using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Pages.Shared;

namespace SmartAttendance.Web.Pages.Documents;

/// <summary>
/// توليد الوثائق وأرشيفها (نظير `/Employees/DocumentBuilderCreation` بكيان).
///
/// ⭐ **المعاينة قبل الاعتماد** هي جوهر الشاشة: توليدٌ جماعي بحقلٍ فارغ يعني مئة
/// شهادة معيبة تُسلَّم قبل أن يلاحظ أحد. فالمعاينة لا تكتب شيئاً بالقاعدة.
/// </summary>
public class GenerateModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public GenerateModel(ApplicationDbContext db, Infrastructure.Security.IProtectedFileService protectedFiles)
    {
        _db = db;
        _protectedFiles = protectedFiles;
    }

    private readonly Infrastructure.Security.IProtectedFileService _protectedFiles;

    [BindProperty(SupportsGet = true)] public int? TemplateId { get; set; }
    [BindProperty(SupportsGet = true)] public int? EmployeeFilter { get; set; }

    [BindProperty] public int SelectedTemplateId { get; set; }
    [BindProperty] public int SelectedEmployeeId { get; set; }
    [BindProperty] public string? Notes { get; set; }
    [BindProperty] public DateOnly IssuedOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>لصق أكواد موظفين للتوليد الجماعي — نفس نمط نطاق المسير.</summary>
    [BindProperty] public string? BulkCodes { get; set; }

    public List<DocumentTemplateStore.Template> Templates { get; private set; } = new();
    public List<DocumentTemplateStore.Generated> Archive { get; private set; } = new();

    /// <summary>رمز الموظف المحسوم واسمه — لتعبئة المنتقي ابتداءً.</summary>
    public string? SelectedEmployeeCode { get; private set; }

    public string? SelectedEmployeeName { get; private set; }

    public string? PreviewHtml { get; private set; }
    public IReadOnlyList<string> PreviewUnresolved { get; private set; } = Array.Empty<string>();
    public string? PreviewEmployeeName { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
        SelectedTemplateId = TemplateId ?? 0;
    }

    /// <summary>معاينة — بلا كتابة بالقاعدة إطلاقاً.</summary>
    public async Task<IActionResult> OnPostPreviewAsync()
    {
        await LoadAsync();

        if (Resolve() is not { } pair)
        {
            return Page();
        }

        var (template, employeeId) = pair;
        var (_, html, unresolved) = await DocumentTemplateStore.GenerateAsync(
            _db, template, employeeId, User.Identity?.Name, Notes, IssuedOn, persist: false, fileUrl: _protectedFiles.BuildUrl);

        // نفس تنقية طريق العرض (View.SafeBody): المعاينة تعرض ناتج القالب خاماً،
        // ونصّ القالب يكتبه HR وقيَم الرموز من بيانات الموظف — فبلا تنقية تصبح XSS.
        PreviewHtml = DocumentHtmlSanitizer.Sanitize(html);
        PreviewUnresolved = unresolved;
        PreviewEmployeeName = SelectedEmployeeName;

        return Page();
    }

    public async Task<IActionResult> OnPostIssueAsync()
    {
        await LoadAsync();

        if (Resolve() is not { } pair)
        {
            return Page();
        }

        var (template, employeeId) = pair;
        var (id, _, unresolved) = await DocumentTemplateStore.GenerateAsync(
            _db, template, employeeId, User.Identity?.Name, Notes, IssuedOn, persist: true, fileUrl: _protectedFiles.BuildUrl);

        var message = unresolved.Count == 0
            ? "صدرت الوثيقة وأُرشفت."
            : $"صدرت الوثيقة — لكن {unresolved.Count} رمزاً بقي بلا قيمة: {string.Join("، ", unresolved)}";

        // ⚠️ الـPIN يُعرض **مرّة واحدة** ولا يُخزَّن خاماً؛ فقدُه يعني إعادة إصدار.
        if (DocumentTemplateStore.LastIssuedPin is { } pin)
        {
            message += $" — رمز PIN للتحقق: {pin} (يُعرض مرّة واحدة فقط، سلّمه لحامل الوثيقة).";
        }

        TempData["SuccessMessage"] = message;
        return RedirectToPage("./View", new { id });
    }

    /// <summary>توليد جماعي بلصق الأكواد — يعيد استعمال مفكّك أكواد المسير حرفياً.</summary>
    public async Task<IActionResult> OnPostBulkAsync()
    {
        await LoadAsync();

        var template = Templates.FirstOrDefault(t => t.Id == SelectedTemplateId);
        if (template is null)
        {
            TempData["ErrorMessage"] = "اختر قالباً أولاً.";
            return Page();
        }

        var codes = PayrollRunScope.ParseCodes(BulkCodes);
        if (codes.Count == 0)
        {
            TempData["ErrorMessage"] = "الصق أكواد الموظفين للتوليد الجماعي.";
            return Page();
        }

        var byCode = PayrollRunScope.BuildCodeMap(await HrmsDatabase.QueryAsync(
            _db,
            "SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo FROM Employees WHERE ISNULL(IsDeleted, 0) = 0;",
            command => { },
            reader => (HrmsDatabase.GetString(reader, "EmployeeNo"), HrmsDatabase.GetInt(reader, "Id"))));

        var (ids, missing) = PayrollRunScope.MatchCodes(codes, byCode);

        var issued = 0;
        var withGaps = 0;

        foreach (var employeeId in ids)
        {
            var (id, _, unresolved) = await DocumentTemplateStore.GenerateAsync(
                _db, template, employeeId, User.Identity?.Name, Notes, IssuedOn, persist: true, source: "جماعي", fileUrl: _protectedFiles.BuildUrl);

            if (id > 0)
            {
                issued++;
                if (unresolved.Count > 0) withGaps++;
            }
        }

        // الكود المفقود يُعرض بنصّه لا كعدد — كودٌ مخطئ واحد يعني موظفاً بلا وثيقة بصمت.
        var message = $"صدرت {issued} وثيقة.";
        if (withGaps > 0) message += $" منها {withGaps} فيها رموز بلا قيمة.";
        if (missing.Count > 0) message += $" وأكواد لم تُطابَق: {string.Join("، ", missing)}";

        TempData["SuccessMessage"] = message;
        return RedirectToPage(new { templateId = SelectedTemplateId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await DocumentTemplateStore.DeleteGeneratedAsync(_db, id);
        TempData["SuccessMessage"] = "حُذفت الوثيقة من الأرشيف.";
        return RedirectToPage(new { templateId = TemplateId });
    }

    private (DocumentTemplateStore.Template Template, int EmployeeId)? Resolve()
    {
        var template = Templates.FirstOrDefault(t => t.Id == SelectedTemplateId);

        if (template is null || SelectedEmployeeId <= 0)
        {
            TempData["ErrorMessage"] = "اختر القالب والموظف.";
            return null;
        }

        return (template, SelectedEmployeeId);
    }

    private async Task LoadAsync()
    {
        Templates = await DocumentTemplateStore.LoadTemplatesAsync(_db, activeOnly: true);
        Archive = await DocumentTemplateStore.LoadGeneratedAsync(_db, templateId: TemplateId);

        var identity = await EmployeePickerLookup.LoadAsync(_db, SelectedEmployeeId);
        SelectedEmployeeCode = identity?.Code;
        SelectedEmployeeName = identity?.Name;
    }
}
