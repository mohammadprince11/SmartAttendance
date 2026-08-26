using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace SmartAttendance.Web.Pages.Documents;

/// <summary>
/// قوالب الوثائق (منشئ الوثائق — م١). نظير `/Setup/DocumentBuilder` بكيان.
///
/// الفجوة التي يسدّها: شهادات الراتب وكتب التعريف وخطابات التجربة كانت تُكتب
/// بـWord **خارج النظام** — بلا مرجع ولا أرشيف ولا بيانات محدَّثة.
/// </summary>
[Authorize(Roles = RoleRouteCatalog.Admin)]
public class TemplatesModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;
    private readonly IFileThreatScanner _threatScanner;
    private readonly MalwareScanningOptions _malwareOptions;

    public TemplatesModel(
        ApplicationDbContext db,
        IWebHostEnvironment environment,
        IFileThreatScanner threatScanner,
        IOptions<MalwareScanningOptions> malwareOptions)
    {
        _db = db;
        _environment = environment;
        _threatScanner = threatScanner;
        _malwareOptions = malwareOptions.Value;
    }

    [BindProperty(SupportsGet = true, Name = "edit")] public int? EditingId { get; set; }
    /// <summary>تصفية القائمة بالنوع: وثائق أم ترويسات أم تذييلات.</summary>
    [BindProperty(SupportsGet = true, Name = "kind")] public string? KindFilter { get; set; }

    [BindProperty] public int TemplateId { get; set; }
    [BindProperty] public string Name { get; set; } = string.Empty;
    [BindProperty] public string? NameEn { get; set; }
    [BindProperty] public string? Description { get; set; }
    [BindProperty] public string? Body { get; set; }
    [BindProperty] public string? RefPrefix { get; set; } = "DOC";
    [BindProperty] public bool AllowEmployeeRequest { get; set; }
    [BindProperty] public bool IsActive { get; set; } = true;
    [BindProperty] public string Conditions { get; set; } = string.Empty;
    [BindProperty] public string Kind { get; set; } = DocumentTemplateStore.KindDocument;
    [BindProperty] public int? HeaderTemplateId { get; set; }
    [BindProperty] public int? FooterTemplateId { get; set; }
    [BindProperty] public IFormFile? StampUpload { get; set; }
    [BindProperty] public bool IsReasonRequired { get; set; }
    [BindProperty] public bool IsAttachmentRequired { get; set; }
    [BindProperty] public bool EnableVerification { get; set; }
    [BindProperty] public string VerificationFactor { get; set; } = DocumentVerificationPolicy.FactorNationalId;
    [BindProperty] public int? VerifyValidDays { get; set; }

    public List<DocumentTemplateStore.Template> Templates { get; private set; } = new();
    public List<DocumentTemplateStore.Template> Headers { get; private set; } = new();
    public List<DocumentTemplateStore.Template> Footers { get; private set; } = new();
    public string? CurrentStampName { get; private set; }
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
            Kind = template.Kind;
            HeaderTemplateId = template.HeaderTemplateId;
            FooterTemplateId = template.FooterTemplateId;
            CurrentStampName = template.StampFileName;
            IsReasonRequired = template.IsReasonRequired;
            IsAttachmentRequired = template.IsAttachmentRequired;
            EnableVerification = template.EnableVerification;
            VerificationFactor = template.VerificationFactor ?? DocumentVerificationPolicy.FactorNationalId;
            VerifyValidDays = template.VerifyValidDays;
            UnknownTokens = DocumentTokenEngine.UnknownTokens(template.Body, Tokens);
        }
        else if (EditingId == 0 && !string.IsNullOrWhiteSpace(KindFilter))
        {
            Kind = KindFilter;
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            TempData["ErrorMessage"] = "اسم القالب إلزامي.";
            return RedirectToPage();
        }

        string? stampKey = null;
        string? stampFileName = null;

        if (StampUpload is { Length: > 0 })
        {
            var extension = Path.GetExtension(StampUpload.FileName);

            // الختم صورة فقط: ملفٌ آخر بمكانه لا يُعرض ويترك وثيقةً بختمٍ مكسور.
            if (!ProtectedFileStore.IsAllowedExtension(extension)
                || extension.ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp"))
            {
                TempData["ErrorMessage"] = "الختم يجب أن يكون صورة (png · jpg · webp).";
                return RedirectToPage(new { edit = TemplateId });
            }

            if (!ProtectedFileStore.IsAllowedSize(StampUpload.Length))
            {
                TempData["ErrorMessage"] = "حجم صورة الختم يتجاوز الحدّ المسموح.";
                return RedirectToPage(new { edit = TemplateId });
            }

            if (!await UploadSignatureValidator.IsValidForExtensionAsync(StampUpload, extension)
                || !FileThreatPolicy.CanStore(
                    _malwareOptions,
                    await FileThreatPolicy.ScanUploadAsync(
                        _threatScanner, StampUpload, HttpContext.RequestAborted)))
            {
                TempData["ErrorMessage"] = "رُفضت صورة الختم بعد فحص المحتوى الأمني.";
                return RedirectToPage(new { edit = TemplateId });
            }

            stampKey = ProtectedFileStore.BuildStorageKey(0, "doc-stamp", extension);
            var root = ProtectedFileStore.ResolveRoot(_environment.ContentRootPath);

            await using var stream = StampUpload.OpenReadStream();
            if (!await ProtectedFileStore.SaveAsync(root, stampKey, stream))
            {
                TempData["ErrorMessage"] = "تعذّر حفظ صورة الختم.";
                return RedirectToPage(new { edit = TemplateId });
            }

            stampFileName = Path.GetFileName(StampUpload.FileName);
        }

        // الترويسة والتذييل لا يحملان ترويسةً ولا ختماً — وإلا صار التداخل لا نهائياً.
        var isDocument = Kind == DocumentTemplateStore.KindDocument;

        var id = await DocumentTemplateStore.SaveTemplateAsync(
            _db, TemplateId, Name.Trim(), NameEn, Description, Body,
            HrConditions.Deserialize(Conditions), RefPrefix, AllowEmployeeRequest, IsActive, User.Identity?.Name,
            Kind,
            isDocument ? HeaderTemplateId : null,
            isDocument ? FooterTemplateId : null,
            isDocument ? stampKey : null,
            isDocument ? stampFileName : null,
            isDocument && IsReasonRequired,
            isDocument && IsAttachmentRequired,
            isDocument && EnableVerification,
            VerificationFactor,
            VerifyValidDays);

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

    /// <summary>صورة الختم — من الجذر المحميّ، لا رابط مباشر.</summary>
    public async Task<IActionResult> OnGetStampAsync(int id)
    {
        var template = await DocumentTemplateStore.FindTemplateAsync(_db, id);
        if (template is null || !template.HasStamp)
        {
            return NotFound();
        }

        var root = ProtectedFileStore.ResolveRoot(_environment.ContentRootPath);
        if (!ProtectedFileStore.TryResolvePhysicalPath(root, template.StampKey, out var path)
            || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(path, ProtectedFileStore.ContentTypeFor(Path.GetExtension(path)));
    }

    private async Task LoadAsync()
    {
        Templates = await DocumentTemplateStore.LoadTemplatesAsync(_db, kind: KindFilter);
        Headers = await DocumentTemplateStore.LoadTemplatesAsync(_db, activeOnly: true, kind: DocumentTemplateStore.KindHeader);
        Footers = await DocumentTemplateStore.LoadTemplatesAsync(_db, activeOnly: true, kind: DocumentTemplateStore.KindFooter);
        CriteriaJson = await HrConditionOptions.BuildCatalogJsonAsync(_db);

        var customFields = await HrConditionFacts.LoadCustomFieldDefinitionsAsync(_db);
        Tokens = DocumentTokenEngine.WithCustomFields(customFields);

        var generated = await DocumentTemplateStore.LoadGeneratedAsync(_db);
        GeneratedCounts = generated
            .GroupBy(row => row.TemplateId)
            .ToDictionary(group => group.Key, group => group.Count());
    }
}
