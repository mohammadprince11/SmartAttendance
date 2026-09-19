using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.PeopleAi;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

[Authorize]
public sealed class SmartOnboardingModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IPeopleAiSessionAccessService _sessionAccess;
    private readonly IOnboardingProtectedAssetService _protectedAssets;
    private readonly PeopleAiWorkerOptions _workerOptions;

    public SmartOnboardingModel(
        ApplicationDbContext db,
        IPeopleAiSessionAccessService sessionAccess,
        IOnboardingProtectedAssetService protectedAssets,
        Microsoft.Extensions.Options.IOptions<PeopleAiWorkerOptions> workerOptions)
    {
        _db = db;
        _sessionAccess = sessionAccess;
        _protectedAssets = protectedAssets;
        _workerOptions = workerOptions.Value;
    }

    public sealed record CompanyOption(int Id, string Name);

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? SessionId { get; set; }

    [BindProperty]
    public IFormFile? DocumentFile { get; set; }

    [BindProperty(SupportsGet = true)]
    public string DeclaredDocumentType { get; set; } = "Unknown";

    public List<CompanyOption> Companies { get; private set; } = [];
    public EmployeeOnboardingStore.SessionRow? Session { get; private set; }
    public List<EmployeeOnboardingStore.DocumentRow> Documents { get; private set; } = [];
    public List<EmployeeDocumentPolicy> DocumentPolicies { get; private set; } = [];
    public List<PeopleAiDocumentTypeDefinition> AvailableDocumentTypes { get; private set; } = [];

    public int QueueRefreshSeconds =>
        Math.Clamp(_workerOptions.PollSeconds * 2, 3, 10);

    public int JobTimeoutSeconds =>
        Math.Clamp(_workerOptions.JobTimeoutSeconds, 30, 900);

    public string? StatusMessage =>
        TempData["SmartOnboardingStatus"]?.ToString();

    public string? ErrorMessage =>
        TempData["SmartOnboardingError"]?.ToString();

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostStartAsync(int companyId)
    {
        var access = await _sessionAccess.ResolveAsync(
            HttpContext,
            HttpContext.RequestAborted);
        if (!access.AllowedCompanyIds.Contains(companyId))
        {
            return Forbid();
        }

        var settings = await PeopleAiSettingsStore.GetAsync(_db, companyId);
        if (!settings.IsEnabled)
        {
            TempData["SmartOnboardingError"] =
                "People AI غير مفعّل لهذه الشركة.";
            return RedirectToPage(new { CompanyId = companyId });
        }

        var sessionId = await EmployeeOnboardingStore.CreateSessionAsync(
            _db,
            companyId,
            access.SystemUserId);

        if (sessionId <= 0)
        {
            TempData["SmartOnboardingError"] =
                "تعذر إنشاء جلسة Smart Onboarding.";
            return RedirectToPage(new { CompanyId = companyId });
        }

        TempData["SmartOnboardingStatus"] =
            "تم إنشاء جلسة Smart Onboarding.";
        return RedirectToPage(new
        {
            CompanyId = companyId,
            SessionId = sessionId
        });
    }
    public async Task<IActionResult> OnPostUploadAsync(
        int companyId,
        long sessionId)
    {
        var access = await _sessionAccess.ResolveAsync(
            HttpContext,
            HttpContext.RequestAborted);
        var session = await EmployeeOnboardingStore.GetSessionAsync(
            _db,
            sessionId);

        if (!_sessionAccess.CanAccessSession(session, companyId, access))
        {
            return Forbid();
        }

        var activeDocumentTypes =
            await PeopleAiSettingsStore.ListDocumentTypesAsync(
                _db,
                companyId);

        var selectedType = activeDocumentTypes.FirstOrDefault(x =>
            x.DocumentType.Equals(
                DeclaredDocumentType,
                StringComparison.OrdinalIgnoreCase));

        if (selectedType is null)
        {
            return UploadResponse(
                false,
                "نوع المستند غير فعال أو غير موجود في إعدادات People AI.",
                companyId,
                sessionId);
        }

        DeclaredDocumentType = selectedType.DocumentType;

        if (DocumentFile is null || DocumentFile.Length <= 0)
        {
            return UploadResponse(
                false,
                "اختر ملف مستند قبل الرفع.",
                companyId,
                sessionId);
        }

        var processingContract = DocumentProcessingContract.Resolve(
            Path.GetExtension(DocumentFile.FileName),
            DeclaredDocumentType);

        if (!processingContract.Format.CanUpload ||
            !processingContract.Format.CanStore)
        {
            return UploadResponse(
                false,
                "تنسيق الملف غير مدعوم للرفع أو التخزين.",
                companyId,
                sessionId);
        }

        var saveResult = await _protectedAssets.SaveAsync(
            DocumentFile,
            companyId,
            sessionId,
            DeclaredDocumentType,
            access.SystemUserId,
            HttpContext.RequestAborted);

        var asset = saveResult.Asset;
        if (asset is null)
        {
            return UploadResponse(
                false,
                UploadRejectionMessage(
                    saveResult.ErrorCode,
                    DocumentFile.Length),
                companyId,
                sessionId);
        }

        var documentId = await EmployeeOnboardingStore.AddDocumentAsync(
            _db,
            sessionId,
            asset.AssetId,
            DeclaredDocumentType,
            processingContract.ShouldQueueAutomaticExtraction);

        var successMessage = processingContract.ShouldQueueAutomaticExtraction
            ? "تم رفع المستند بأمان وإضافته إلى طابور المعالجة المحلية."
            : "تم رفع المستند وحفظه بأمان للمراجعة. الاستخراج التلقائي غير مدعوم لهذا التنسيق حالياً.";

        return UploadResponse(
            documentId > 0,
            documentId > 0
                ? successMessage
                : "حُفظ الملف بأمان لكن تعذر تسجيل المستند في جلسة الـOnboarding.",
            companyId,
            sessionId,
            documentId > 0 ? documentId : null);
    }
    public async Task<IActionResult> OnPostRetryDocumentAsync(
        int companyId,
        long sessionId,
        long documentId)
    {
        var access = await _sessionAccess.ResolveAsync(
            HttpContext,
            HttpContext.RequestAborted);
        var session = await EmployeeOnboardingStore.GetSessionAsync(
            _db,
            sessionId);

        if (!_sessionAccess.CanAccessSession(session, companyId, access))
        {
            return Forbid();
        }

        var requeued =
            await EmployeeOnboardingStore.RequeueFailedDocumentAsync(
                _db,
                sessionId,
                documentId);

        TempData[requeued
            ? "SmartOnboardingStatus"
            : "SmartOnboardingError"] = requeued
                ? "تمت إعادة المستند إلى طابور المعالجة."
                : "لا يمكن إعادة المستند؛ تحقق من حالته.";

        return RedirectToPage(new
        {
            CompanyId = companyId,
            SessionId = sessionId
        });
    }

    public async Task<IActionResult> OnPostCancelAsync(
        int companyId,
        long sessionId)
    {
        var access = await _sessionAccess.ResolveAsync(
            HttpContext,
            HttpContext.RequestAborted);
        var session = await EmployeeOnboardingStore.GetSessionAsync(
            _db,
            sessionId);

        if (!_sessionAccess.CanAccessSession(session, companyId, access))
        {
            return Forbid();
        }

        await EmployeeOnboardingStore.CancelSessionAsync(
            _db,
            sessionId);

        TempData["SmartOnboardingStatus"] =
            "تم إلغاء جلسة Smart Onboarding.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    private IActionResult UploadResponse(
        bool success,
        string message,
        int companyId,
        long sessionId,
        long? documentId = null)
    {
        if (string.Equals(
                Request.Headers["X-Requested-With"].ToString(),
                "ZynoraAjaxUpload",
                StringComparison.Ordinal))
        {
            return new JsonResult(new
            {
                success,
                message,
                documentId,
                documentType = DeclaredDocumentType
            });
        }

        TempData[success
            ? "SmartOnboardingStatus"
            : "SmartOnboardingError"] = message;

        return RedirectToPage(new
        {
            CompanyId = companyId,
            SessionId = sessionId,
            DeclaredDocumentType
        });
    }

    private static string UploadRejectionMessage(
        string? errorCode,
        long fileSizeBytes)
    {
        var sizeMb = fileSizeBytes / 1024d / 1024d;

        return errorCode switch
        {
            ProtectedOnboardingAssetErrorCodes.FileTooLarge =>
                $"حجم الملف {sizeMb:0.0} MB ويتجاوز الحد الأقصى 10 MB. قلّل حجم الصورة أو احفظها بجودة أقل ثم أعد الرفع.",
            ProtectedOnboardingAssetErrorCodes.ExtensionNotAllowed =>
                "امتداد الملف غير مدعوم. استخدم PDF أو JPG/JPEG أو PNG أو WEBP أو ملف Office مدعوماً.",
            ProtectedOnboardingAssetErrorCodes.SignatureMismatch =>
                "محتوى الملف الحقيقي لا يطابق امتداده. أعد حفظ الصورة فعلياً بصيغة JPG أو PNG ثم أعد الرفع؛ لا يكفي تغيير اسم الامتداد.",
            ProtectedOnboardingAssetErrorCodes.MalwareThreat =>
                "تم رفض الملف لأن فحص الأمان اكتشف تهديداً.",
            ProtectedOnboardingAssetErrorCodes.MalwareScanUnavailable =>
                "تعذر الوصول إلى محرك فحص الملفات، والسياسة الحالية تمنع التخزين بدون فحص ناجح.",
            ProtectedOnboardingAssetErrorCodes.MalwareScanError =>
                "حدث خطأ أثناء فحص أمان الملف. أعد المحاولة أو راجع خدمة الفحص.",
            ProtectedOnboardingAssetErrorCodes.SessionInvalid =>
                "جلسة Smart Onboarding لم تعد تقبل مستندات جديدة. أنشئ جلسة جديدة أو حدّث الصفحة.",
            ProtectedOnboardingAssetErrorCodes.StorageFailed =>
                "نجح التحقق من الملف لكن تعذر حفظه في التخزين المحمي.",
            _ =>
                "تم رفض الملف قبل التخزين. تحقق من الملف ثم أعد المحاولة."
        };
    }

    private async Task LoadAsync()
    {
        var access = await _sessionAccess.ResolveAsync(
            HttpContext,
            HttpContext.RequestAborted);

        Companies = await _db.Companies
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.IsActive &&
                access.AllowedCompanyIds.Contains(x.Id))
            .OrderBy(x => x.Name)
            .Select(x => new CompanyOption(x.Id, x.Name))
            .ToListAsync(HttpContext.RequestAborted);

        CompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            CompanyId,
            Companies.Select(x => x.Id).ToArray());

        if (CompanyId is not > 0 ||
            !access.AllowedCompanyIds.Contains(CompanyId.Value))
        {
            return;
        }

        DocumentPolicies =
            await PeopleAiSettingsStore.ListDocumentPoliciesAsync(
                _db,
                CompanyId.Value);

        AvailableDocumentTypes =
            await PeopleAiSettingsStore.ListDocumentTypesAsync(
                _db,
                CompanyId.Value);

        if (!AvailableDocumentTypes.Any(x =>
                x.DocumentType.Equals(
                    DeclaredDocumentType,
                    StringComparison.OrdinalIgnoreCase)))
        {
            DeclaredDocumentType =
                AvailableDocumentTypes.FirstOrDefault()?.DocumentType ??
                PeopleAiDocumentTypes.Unknown;
        }

        if (SessionId is not > 0)
        {
            return;
        }
        Session = await EmployeeOnboardingStore.GetSessionAsync(
            _db,
            SessionId.Value);

        if (!_sessionAccess.CanAccessSession(Session, CompanyId.Value, access))
        {
            Session = null;
            Documents = [];
            ErrorPageMessage =
                "الجلسة غير موجودة أو لا تملك صلاحية الوصول إليها.";
            return;
        }

        Documents = await EmployeeOnboardingStore.ListDocumentsAsync(
            _db,
            Session!.Id);
    }

    public string? ErrorPageMessage { get; private set; }
}
