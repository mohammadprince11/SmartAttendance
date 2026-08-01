using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

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
    private readonly IWebHostEnvironment _environment;

    public ViewModel(ApplicationDbContext db, IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

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
    public string SafeHeader() => DocumentHtmlSanitizer.Sanitize(Document?.HeaderHtml);
    public string SafeFooter() => DocumentHtmlSanitizer.Sanitize(Document?.FooterHtml);

    /// <summary>رابط التحقق العامّ المطبوع على الوثيقة (وهو ما سيحمله رمز QR لاحقاً).</summary>
    public string VerifyUrl() =>
        Document?.VerifyToken is { } token
            ? $"{Request.Scheme}://{Request.Host}/Verify?token={Uri.EscapeDataString(token)}"
            : string.Empty;

    /// <summary>سحب الوثيقة — يُبطل تحقّقها فوراً بلا حذفها من الأرشيف.</summary>
    public async Task<IActionResult> OnPostRevokeAsync(int id)
    {
        await DocumentTemplateStore.RevokeAsync(_db, id, User.Identity?.Name);
        TempData["SuccessMessage"] = "سُحبت الوثيقة — صار التحقق منها يُظهر أنها مسحوبة.";
        return RedirectToPage(new { id });
    }

    /// <summary>يودع الوثيقة ملف الموظف — إيداعٌ صريح لا تلقائي.</summary>
    public async Task<IActionResult> OnPostFileAsync(int id)
    {
        var filed = await DocumentTemplateStore.FileToEmployeeAsync(_db, id, User.Identity?.Name);

        TempData["SuccessMessage"] = filed
            ? "أُودعت الوثيقة بملف الموظف."
            : "الوثيقة مودعة أصلاً بملف الموظف.";

        return RedirectToPage(new { id });
    }

    /// <summary>ختم الوثيقة الصادرة — من الجذر المحميّ، والمفتاح مخزَّن بالوثيقة نفسها.</summary>
    public async Task<IActionResult> OnGetStampAsync(int id)
    {
        var document = await DocumentTemplateStore.FindGeneratedAsync(_db, id);
        if (document is null || !document.HasStamp)
        {
            return NotFound();
        }

        var root = ProtectedFileStore.ResolveRoot(_environment.ContentRootPath);
        if (!ProtectedFileStore.TryResolvePhysicalPath(root, document.StampKey, out var path)
            || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(path, ProtectedFileStore.ContentTypeFor(Path.GetExtension(path)));
    }
}
