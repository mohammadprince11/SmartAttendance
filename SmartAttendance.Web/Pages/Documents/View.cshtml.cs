using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Documents;

/// <summary>
/// عرض وثيقة صادرة بصيغة جاهزة للطباعة (Ctrl+P ⟵ حفظ كـPDF).
///
/// ⚠️ **لا مكتبة PDF بالمشروع**، وإضافة واحدة قرارُ ترخيصٍ ومنتج لا قرارُ تنفيذ.
/// فمخرَج م١ صفحة طباعة نظيفة: النتيجة PDF فعليّ بيد المستخدم بلا اعتمادية جديدة،
/// وترقيتها لمولّد خادمي لاحقاً لا تمسّ شيئاً من هذه الطبقة.
/// </summary>
public class ViewModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ViewModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public int Id { get; set; }

    public DocumentTemplateStore.Generated? Document { get; private set; }
    public string? CompanyName { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Document = await DocumentTemplateStore.FindGeneratedAsync(_db, Id);

        if (Document is null)
        {
            return NotFound();
        }

        CompanyName = await HrmsDatabase.ScalarAsync<string>(
            _db,
            """
SELECT TOP 1 c.Name
FROM Employees e
INNER JOIN Companies c ON c.Id = e.CompanyId
WHERE e.Id = @EmployeeId;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", Document.EmployeeId));

        return Page();
    }

    /// <summary>
    /// النصّ المخزَّن يُنقّى **مرّة أخرى عند العرض** لا اعتماداً على تنقية الحفظ وحدها.
    /// السبب: صفوف قد تكون كُتبت قبل وجود المنقّح أو بمسار آخر، والدفاع بالعمق يعني
    /// ألا يكون الأمن معتمداً على نقطة واحدة.
    /// </summary>
    public string SafeBody() => DocumentHtmlSanitizer.Sanitize(Document?.BodyHtml);
}
