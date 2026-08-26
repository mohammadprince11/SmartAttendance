using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeePortal;

/// <summary>
/// بوابة الموظف — تعبئة نموذج (طلب مخصص أو استبيان).
///
/// هذه الصفحة هي ما يجعل باني النماذج **مستعمَلاً** لا معروضاً: بدونها تبقى
/// القوالب تعريفاتٍ بلا إجابات.
/// </summary>
public class FormFillModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public FormFillModel(ApplicationDbContext db) => _db = db;

    [TempData] public string? StatusMessage { get; set; }

    [BindProperty(SupportsGet = true, Name = "form")] public int? FormId { get; set; }
    [BindProperty] public Guid SubmissionToken { get; set; }

    public List<FormTemplateStore.Template> Available { get; private set; } = new();
    public FormTemplateStore.Template? Current { get; private set; }
    public List<FormTemplateStore.Group> Groups { get; private set; } = new();
    public List<FormTemplateStore.Field> Fields { get; private set; } = new();
    public List<FormSubmissionStore.Submission> Mine { get; private set; } = new();
    public List<string> Errors { get; private set; } = new();

    public List<(FormTemplateStore.Group? Group, List<FormTemplateStore.Field> Fields)> Layout()
    {
        var layout = Groups
            .Select(group => ((FormTemplateStore.Group?)group, Fields.Where(f => f.GroupId == group.Id).ToList()))
            .Where(entry => entry.Item2.Count > 0)
            .ToList();

        var ungrouped = Fields.Where(field => field.IsUngrouped).ToList();
        if (ungrouped.Count > 0) layout.Add((null, ungrouped));

        return layout;
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "تعذّر تحديد هويتك.";
            return RedirectToPage();
        }

        await LoadAsync();

        // ⚠️ الأهلية تُعاد بالخادم — نموذجٌ معدَّل يدوياً كان يعبّئ أي قالب بمعرّفه.
        if (Current is null || !Available.Any(template => template.Id == Current.Id))
        {
            StatusMessage = "هذا النموذج غير متاح لك.";
            return RedirectToPage();
        }

        var answers = new List<(int FieldId, string Label, string ControlType, string? Value, int SortOrder)>();
        var toValidate = new List<(string, string?, bool, string?, string?)>();

        foreach (var field in Fields)
        {
            var raw = Request.Form[$"f_{field.Id}"];
            // المتعدد يصل قيماً مكرّرة بنفس الاسم — تُدمج بفاصلة كما يقرأها المحرّك.
            var value = field.ControlType == FormBuilder.ControlMultiSelect
                ? string.Join(", ", raw.Where(v => !string.IsNullOrWhiteSpace(v)))
                : raw.FirstOrDefault();

            answers.Add((field.Id, field.Label, field.ControlType, value, field.SortOrder));
            toValidate.Add((field.Label, field.ControlType, field.IsRequired, field.Options, value));
        }

        Errors = FormBuilder.ValidateSubmission(toValidate);

        if (Errors.Count > 0)
        {
            // تُعرض **كلها** لا أوّلها، والصفحة تبقى مفتوحة فلا تضيع الإجابات المكتوبة.
            return Page();
        }

        var submitted = await FormSubmissionStore.SubmitAsync(
            _db, Current, employeeId, answers, User.Identity?.Name, SubmissionToken);

        StatusMessage = Current.IsSurvey
            ? "شكراً — سُجِّلت إجاباتك."
            : submitted.Workflow?.Ok == true
                ? submitted.IsDuplicate
                    ? $"سبق استلام طلبك رقم {submitted.RequestId} — لم ننشئ نسخة مكررة."
                    : $"أُرسل طلبك رقم {submitted.RequestId} وبدأ مسار الموافقة."
                : $"حُفظ طلبك رقم {submitted.RequestId}، لكن تعذّر بدء الموافقة: {submitted.Workflow?.Message ?? "خطأ غير متوقع"}";
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        if (SubmissionToken == Guid.Empty) SubmissionToken = Guid.NewGuid();
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0) return;

        Available = await FormSubmissionStore.AvailableForAsync(_db, employeeId, DateOnly.FromDateTime(DateTime.Today));
        Mine = await FormSubmissionStore.LoadAsync(_db, employeeId: employeeId);

        if (FormId is > 0)
        {
            Current = Available.FirstOrDefault(template => template.Id == FormId.Value);

            if (Current is not null)
            {
                Groups = await FormTemplateStore.LoadGroupsAsync(_db, Current.Id);
                Fields = await FormTemplateStore.LoadFieldsAsync(_db, Current.Id);
            }
        }
    }

    private async Task<int> ResolveEmployeeIdAsync()
    {
        var employeeIdClaim = User.FindFirstValue("EmployeeId");
        if (int.TryParse(employeeIdClaim, out var claimEmployeeId) && claimEmployeeId > 0)
            return claimEmployeeId;

        var username = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(username))
            return await HrmsDatabase.ScalarAsync<int>(
                _db,
                "SELECT TOP 1 ISNULL(EmployeeId, 0) FROM AppLoginUsers WHERE Username = @Username AND IsActive = 1",
                command => HrmsDatabase.AddParameter(command, "@Username", username));
        return 0;
    }
}
