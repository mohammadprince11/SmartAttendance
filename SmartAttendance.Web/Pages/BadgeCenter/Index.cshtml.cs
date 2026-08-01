using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.BadgeCenter;

/// <summary>
/// مركز بطاقات الموظفين — نظير <c>/Employees/BadgeCenter</c> بكيان.
///
/// ⭐ **بلا مودل جديد**: قالب البطاقة هو قالب وثيقة بنوع <c>Badge</c>، فيرث
/// المحرّر والرموز وشروط الجمهور والنُسَخ المشروطة والمعاينة كما هي. والفرق
/// الوحيد عن كيان أن سطح التصميم عندهم SVG وعندنا HTML — قرارٌ مقصود لا نقص.
///
/// ⚠️ **التصدير طباعةٌ لا PDF**: مولّد PDF خادميّ قرارُ ترخيص لم يُحسم بعد، فبدل
/// تعطيل الشاشة كلّها انتظاراً له، الصفحة **مهيّأة للطباعة** بمقاس بطاقة قياسي
/// (85.6×54مم) — والمتصفح يطبع أو يحفظ PDF. حين تُختار المكتبة يصير التصدير زراً
/// إضافياً لا إعادة بناء.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public int TemplateId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    /// <summary>معرّفات الموظفين المطلوب طبع بطاقاتهم — تُمرَّر بالرابط ليكون العرض قابلاً للمشاركة.</summary>
    [BindProperty(SupportsGet = true, Name = "emp")] public List<int> EmployeeIds { get; set; } = new();

    public List<DocumentTemplateStore.Template> Templates { get; set; } = new();
    public DocumentTemplateStore.Template? Current { get; set; }

    public List<(int Id, string Label)> Candidates { get; set; } = new();

    /// <summary>البطاقات المرسومة: اسم الموظف + HTML الناتج + رموزٌ بقيت بلا قيمة.</summary>
    public List<(string Employee, string Html, IReadOnlyList<string> Unresolved)> Cards { get; set; } = new();

    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        Templates = await DocumentTemplateStore.LoadTemplatesAsync(
            _db, activeOnly: true, kind: DocumentTemplateStore.KindBadge);

        Candidates = await LoadCandidatesAsync();

        if (TemplateId <= 0)
        {
            return;
        }

        Current = Templates.FirstOrDefault(t => t.Id == TemplateId);
        if (Current is null)
        {
            Message = "القالب غير موجود أو غير مفعّل.";
            return;
        }

        if (EmployeeIds.Count == 0)
        {
            return;
        }

        foreach (var employeeId in EmployeeIds.Distinct().Take(200))
        {
            // نفس محرّك توليد الوثائق، وبـ persist:false: البطاقة تُطبع ولا تُؤرشَف
            // كوثيقة برقم مرجعي — أرشفة ألف بطاقة بكل طبعة ضجيجٌ لا سجلّ.
            var (_, html, unresolved) = await DocumentTemplateStore.GenerateAsync(
                _db,
                Current,
                employeeId,
                User.Identity?.Name,
                notes: null,
                issuedOn: DateOnly.FromDateTime(DateTime.Today),
                persist: false,
                source: "بطاقة");

            if (string.IsNullOrWhiteSpace(html))
            {
                continue;
            }

            var label = Candidates.FirstOrDefault(c => c.Id == employeeId).Label ?? employeeId.ToString();
            Cards.Add((label, html, unresolved));
        }
    }

    private async Task<List<(int Id, string Label)>> LoadCandidatesAsync()
    {
        var like = string.IsNullOrWhiteSpace(Search) ? null : "%" + Search.Trim() + "%";
        var filter = like is null ? string.Empty : " AND (FullName LIKE @Q OR EmployeeNo LIKE @Q)";

        return await HrmsDatabase.QueryAsync(
            _db,
            $"""
SELECT TOP 300 Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName
FROM Employees
WHERE ISNULL(IsDeleted, 0) = 0 AND ISNULL(IsActive, 1) = 1{filter}
ORDER BY EmployeeNo;
""",
            command =>
            {
                if (like is not null) HrmsDatabase.AddParameter(command, "@Q", like);
            },
            reader => (
                HrmsDatabase.GetInt(reader, "Id"),
                $"{HrmsDatabase.GetString(reader, "EmployeeNo")} — {HrmsDatabase.GetString(reader, "FullName")}"));
    }
}
