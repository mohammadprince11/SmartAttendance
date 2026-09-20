using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Localization;
using SmartAttendance.Web.Infrastructure.PeopleAi;
using SmartAttendance.Web.Infrastructure.Security;
using SmartAttendance.Web.Infrastructure.Ui;

namespace SmartAttendance.Web.Pages.Employees;

[Authorize]
public sealed class SmartOnboardingReviewModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly IPeopleAiSessionAccessService _sessionAccess;
    private readonly IPermissionAuthorizationService _permissionAuthorization;
    private readonly IEmployeeService _employeeService;
    private readonly ICompanyDataLocalizationService _dataLocalization;
    private readonly IOnboardingProtectedAssetService _protectedAssets;
    private readonly IProtectedFileService _protectedFiles;

    public SmartOnboardingReviewModel(
        ApplicationDbContext db,
        IPeopleAiSessionAccessService sessionAccess,
        IPermissionAuthorizationService permissionAuthorization,
        IEmployeeService employeeService,
        ICompanyDataLocalizationService dataLocalization,
        IOnboardingProtectedAssetService protectedAssets,
        IProtectedFileService protectedFiles)
    {
        _db = db;
        _sessionAccess = sessionAccess;
        _permissionAuthorization = permissionAuthorization;
        _employeeService = employeeService;
        _dataLocalization = dataLocalization;
        _protectedAssets = protectedAssets;
        _protectedFiles = protectedFiles;
    }

    public sealed class FinalizeEmployeeInput
    {
        [StringLength(50)]
        public string EmployeeNo { get; set; } = string.Empty;

        [Required, StringLength(200)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(100)] public string? FirstName { get; set; }
        [StringLength(100)] public string? SecondName { get; set; }
        [StringLength(100)] public string? ThirdName { get; set; }
        [StringLength(100)] public string? LastName { get; set; }

        [StringLength(100)] public string? FirstNameEn { get; set; }
        [StringLength(100)] public string? SecondNameEn { get; set; }
        [StringLength(100)] public string? ThirdNameEn { get; set; }
        [StringLength(100)] public string? LastNameEn { get; set; }

        public bool IsCitizen { get; set; } = true;

        [StringLength(50)]
        public string? NationalId { get; set; }

        [StringLength(50)]
        public string? FamilyNumber { get; set; }

        [StringLength(50)]
        public string? PassportNo { get; set; }

        public DateOnly? BirthDate { get; set; }

        [StringLength(100)]
        public string? Nationality { get; set; }

        [StringLength(100)]
        public string? Country { get; set; }

        [StringLength(30)]
        public string? Gender { get; set; }

        [StringLength(50)]
        public string? MaritalStatus { get; set; }

        [StringLength(150)]
        public string? SponsorName { get; set; }

        [StringLength(50)]
        public string? Religion { get; set; }

        [StringLength(100)]
        public string? MotherCountry { get; set; }

        [StringLength(100)]
        public string? MotherCity { get; set; }

        [StringLength(50)]
        public string? Phone { get; set; }

        [StringLength(20)]
        public string? PhoneExtension { get; set; }

        [EmailAddress, StringLength(200)]
        public string? Email { get; set; }

        [EmailAddress, StringLength(200)]
        public string? PersonalEmail { get; set; }

        [Required]
        public DateOnly HireDate { get; set; } =
            DateOnly.FromDateTime(DateTime.Today);

        [Range(1, int.MaxValue)]
        public int BranchId { get; set; }

        [Range(1, int.MaxValue)]
        public int DepartmentId { get; set; }

        public int? PositionId { get; set; }

        public DateOnly? JoiningDate { get; set; }

        [StringLength(50)]
        public string? WorkType { get; set; }

        [StringLength(100)]
        public string? JobGrade { get; set; }

        public int? DirectManagerId { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public sealed record ManagerOption(
        int Id,
        string EmployeeNo,
        string FullName);

    public sealed record DuplicateView(
        long DocumentId,
        string DocumentType,
        string RuleCode,
        string NormalizedNumber,
        PeopleAiDuplicateAction Action,
        IReadOnlyList<IdentityDuplicateCandidate> Candidates);

    public sealed record CrossDocumentValue(
        long DocumentId,
        string DocumentType,
        string DocumentName,
        string Value,
        decimal? Confidence,
        string ReviewStatus);

    public sealed record CrossDocumentComparison(
        string FieldKey,
        string Severity,
        bool HasConflict,
        IReadOnlyList<CrossDocumentValue> Values);

    private static readonly IReadOnlyDictionary<string, string>
        CrossDocumentTrackedFields =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DateOfBirth"] = "Blocking",
                ["NationalNumber"] = "Blocking",
                ["FamilyNumber"] = "Blocking",
                ["FirstName"] = "Warning",
                ["SecondName"] = "Warning",
                ["ThirdName"] = "Warning",
                ["LastName"] = "Warning",
                ["MotherName"] = "Warning"
            };

    [BindProperty(SupportsGet = true)]
    public int CompanyId { get; set; }

    [BindProperty(SupportsGet = true)]
    public long SessionId { get; set; }

    [BindProperty]
    public FinalizeEmployeeInput Finalize { get; set; } = new();

    public EmployeeOnboardingStore.SessionRow? Session { get; private set; }
    public List<EmployeeOnboardingStore.DocumentRow> Documents { get; private set; } = [];
    public List<PeopleAiReviewStore.ReviewField> Fields { get; private set; } = [];
    public List<PeopleAiStructuredRecordStore.StructuredRecordRow> StructuredRecords { get; private set; } = [];
    public List<PeopleAiReviewStore.ValidationIssue> Issues { get; private set; } = [];
    public List<EmployeeDocumentPolicy> DocumentPolicies { get; private set; } = [];
    public List<PeopleAiFieldPolicy> FieldPolicies { get; private set; } = [];
    public List<DuplicateView> DuplicateMatches { get; private set; } = [];
    public List<CrossDocumentComparison> CrossDocumentComparisons { get; private set; } = [];
    public List<SmartAttendance.Application.Branches.ViewModels.BranchListViewModel> Branches { get; private set; } = [];
    public List<SmartAttendance.Application.Departments.ViewModels.DepartmentListViewModel> Departments { get; private set; } = [];
    public List<PositionOptionViewModel> Positions { get; private set; } = [];
    public List<string> ReligionOptions { get; private set; } = [];
    public List<string> WorkTypeOptions { get; private set; } = [];
    public List<string> GradeOptions { get; private set; } = [];
    public List<string> SponsorOptions { get; private set; } = [];
    public List<ManagerOption> ManagerOptions { get; private set; } = [];

    public bool CanPreview(
        EmployeeOnboardingStore.DocumentRow document) =>
        DocumentProcessingContract.Resolve(
            Path.GetExtension(document.OriginalFileName),
            document.DeclaredDocumentType).Format.CanPreview;

    public bool CanReview { get; private set; }
    public bool CanVerifyOriginal { get; private set; }
    public bool CanCreateEmployee { get; private set; }
    public bool CanMarkReady { get; private set; }
    public bool CodeSchemaActive { get; private set; }
    public string? CodeSchemaPreview { get; private set; }
    public string? DocumentPolicyError { get; private set; }
    public string? PageError { get; private set; }

    public string? StatusMessage =>
        TempData["SmartOnboardingReviewStatus"]?.ToString();

    public string? ErrorMessage =>
        TempData["SmartOnboardingReviewError"]?.ToString();

    public async Task OnGetAsync()
    {
        await LoadAsync(initializeFinalize: true);
    }

    public async Task<IActionResult> OnPostReviewFieldAsync(
        long fieldId,
        string action,
        string? reviewedValue)
    {
        var context = await GetAuthorizedContextAsync(requireReviewer: true);
        if (context is null)
        {
            return Forbid();
        }

        if (fieldId <= 0 ||
            action is not ("Accept" or "Modify" or "Reject"))
        {
            TempData["SmartOnboardingReviewError"] =
                "إجراء مراجعة الحقل غير صالح.";
            return RedirectToSelf();
        }

        await PeopleAiReviewStore.ReviewFieldAsync(
            _db,
            SessionId,
            fieldId,
            context.Value.Access.SystemUserId!.Value,
            action,
            reviewedValue);

        TempData["SmartOnboardingReviewStatus"] =
            action == "Reject"
                ? "تم رفض الحقل."
                : "تم اعتماد الحقل.";
        return RedirectToSelf();
    }


    public async Task<IActionResult> OnPostReviewStructuredRecordAsync(
        long recordId,
        string action,
        string? title,
        string? subtitle,
        string? country,
        string? refNo,
        DateOnly? fromDate,
        DateOnly? toDate,
        bool isCurrent,
        string? note)
    {
        var context = await GetAuthorizedContextAsync(requireReviewer: true);
        if (context is null)
        {
            return Forbid();
        }

        if (recordId <= 0 ||
            action is not ("Accept" or "Modify" or "Reject"))
        {
            TempData["SmartOnboardingReviewError"] =
                "إجراء مراجعة السجل غير صالح.";
            return RedirectToSelf();
        }

        await PeopleAiStructuredRecordStore.ReviewAsync(
            _db,
            SessionId,
            recordId,
            context.Value.Access.SystemUserId!.Value,
            action,
            new PeopleAiStructuredRecordStore.ReviewInput(
                title,
                subtitle,
                country,
                refNo,
                fromDate,
                toDate,
                isCurrent,
                note));

        TempData["SmartOnboardingReviewStatus"] =
            action == "Reject"
                ? "تم رفض السجل المستخرج."
                : "تم اعتماد السجل المستخرج.";

        return RedirectToSelf();
    }

    public async Task<IActionResult> OnPostAcceptAllAsync()
    {
        var context = await GetAuthorizedContextAsync(requireReviewer: true);
        if (context is null)
        {
            return Forbid();
        }

        await PeopleAiReviewStore.EnsureConfiguredFieldsAsync(
            _db,
            CompanyId,
            SessionId);

        var accepted =
            await PeopleAiReviewStore.AcceptHighConfidenceConfiguredFieldsAsync(
                _db,
                CompanyId,
                SessionId,
                context.Value.Access.SystemUserId!.Value);

        await PeopleAiStructuredRecordStore.AcceptHighConfidenceAsync(
            _db,
            SessionId,
            context.Value.Access.SystemUserId!.Value);

        TempData["SmartOnboardingReviewStatus"] =
            accepted > 0
                ? $"تم اعتماد {accepted} حقل وسجلات CV عالية الثقة (90% فأعلى)."
                : "تم تطبيق اعتماد الثقة العالية على الحقول وسجلات CV.";

        return RedirectToSelf();
    }

    public async Task<IActionResult> OnPostUpdateDocumentMetaAsync(
        long documentId,
        DateOnly? expiryDate)
    {
        var context = await GetAuthorizedContextAsync(requireReviewer: true);
        if (context is null)
        {
            return Forbid();
        }

        var documents = await EmployeeOnboardingStore.ListDocumentsAsync(
            _db,
            SessionId);

        if (!documents.Any(x => x.Id == documentId))
        {
            return Forbid();
        }

        await PeopleAiReviewStore.SetReviewedExpiryDateAsync(
            _db,
            SessionId,
            documentId,
            expiryDate);

        TempData["SmartOnboardingReviewStatus"] =
            "تم تحديث بيانات المستند.";
        return RedirectToSelf();
    }

    public async Task<IActionResult> OnPostVerifyOriginalAsync(
        long documentId,
        string action)
    {
        var context = await GetAuthorizedContextAsync(requireReviewer: false);
        if (context is null)
        {
            return Forbid();
        }

        if (!await CanVerifyOriginalAsync(context.Value.Access))
        {
            return Forbid();
        }

        if (documentId <= 0 ||
            action is not ("Seen" or "Verified" or "Rejected"))
        {
            TempData["SmartOnboardingReviewError"] =
                "إجراء التحقق من أصل المستند غير صالح.";
            return RedirectToSelf();
        }

        var documents = await EmployeeOnboardingStore.ListDocumentsAsync(
            _db,
            SessionId);

        if (!documents.Any(x => x.Id == documentId))
        {
            return Forbid();
        }

        await PeopleAiReviewStore.SetOriginalVerificationAsync(
            _db,
            SessionId,
            documentId,
            context.Value.Access.SystemUserId!.Value,
            action);

        TempData["SmartOnboardingReviewStatus"] =
            action switch
            {
                "Verified" => "تم التحقق من أصل المستند.",
                "Seen" => "تم تسجيل الاطلاع على أصل المستند.",
                _ => "تم رفض مطابقة أصل المستند."
            };

        return RedirectToSelf();
    }

    public async Task<IActionResult> OnPostResolveIssueAsync(
        long issueId,
        string? resolution)
    {
        var context = await GetAuthorizedContextAsync(requireReviewer: true);
        if (context is null)
        {
            return Forbid();
        }

        var issues = await PeopleAiReviewStore.ListIssuesAsync(
            _db,
            SessionId);
        var issue = issues.FirstOrDefault(x => x.Id == issueId);

        if (issue is null)
        {
            TempData["SmartOnboardingReviewError"] =
                "المشكلة غير موجودة.";
            return RedirectToSelf();
        }

        if (issue.RuleCode.StartsWith(
                "DUPLICATE_BLOCK_",
                StringComparison.Ordinal))
        {
            TempData["SmartOnboardingReviewError"] =
                "هذه المطابقة مانعة حسب سياسة الشركة ولا يمكن تجاوزها يدوياً.";
            return RedirectToSelf();
        }

        if (string.IsNullOrWhiteSpace(resolution))
        {
            TempData["SmartOnboardingReviewError"] =
                "اكتب قرار المراجع قبل إغلاق المشكلة.";
            return RedirectToSelf();
        }

        await PeopleAiReviewStore.ResolveIssueAsync(
            _db,
            SessionId,
            issueId,
            context.Value.Access.SystemUserId!.Value,
            resolution);

        TempData["SmartOnboardingReviewStatus"] =
            "تم توثيق قرار المراجع وإغلاق المشكلة.";
        return RedirectToSelf();
    }

    public async Task<IActionResult> OnPostMarkReadyAsync()
    {
        var context = await GetAuthorizedContextAsync(requireReviewer: true);
        if (context is null)
        {
            return Forbid();
        }

        // Required onboarding documents belong to final employee creation,
        // not to the human-review completion gate.
        // Review may reach Ready while Contract is still pending.
        // Finalize validates required documents again before employee creation.

        await RefreshCrossDocumentIssuesAsync(
            persistIssues: true);

        await RefreshDuplicateIssuesAsync(
            context.Value.Access,
            context.Value.Settings);

        if (await PeopleAiStructuredRecordStore.HasPendingLatestAsync(
                _db,
                SessionId))
        {
            TempData["SmartOnboardingReviewError"] =
                "راجع سجلات CV المستخرجة (الخبرة والتعليم والشهادات) قبل تحويل الجلسة إلى Ready.";
            return RedirectToSelf();
        }

        var ready = await PeopleAiReviewStore.MarkReadyAsync(
            _db,
            SessionId);

        if (!ready)
        {
            TempData["SmartOnboardingReviewError"] =
                "لا يمكن اعتماد الجلسة الآن. راجع الحقول المعلقة والمشاكل المانعة.";
        }
        else
        {
            TempData["SmartOnboardingReviewStatus"] =
                "تم اعتماد المراجعة. الجلسة جاهزة للمعاينة النهائية وإنشاء الموظف.";
        }

        return RedirectToSelf();
    }

    public async Task<IActionResult> OnPostFinalizeAsync(int departmentSelectionId)
    {
        var context = await GetAuthorizedContextAsync(requireReviewer: true);
        if (context is null)
        {
            return Forbid();
        }

        if (!await CanCreateEmployeeAsync(context.Value.Access))
        {
            return Forbid();
        }

        if (context.Value.Session.Status == "Completed" &&
            context.Value.Session.CreatedEmployeeId is > 0)
        {
            return RedirectToPage(
                "/Employees/Profile",
                new { id = context.Value.Session.CreatedEmployeeId.Value });
        }

        if (context.Value.Session.Status != "Ready")
        {
            TempData["SmartOnboardingReviewError"] =
                "الجلسة ليست في حالة Ready.";
            return RedirectToSelf();
        }

        var resolvedDepartmentId =
            departmentSelectionId > 0
                ? departmentSelectionId
                : Finalize.DepartmentId;

        if (resolvedDepartmentId > 0)
        {
            Finalize.DepartmentId = resolvedDepartmentId;
            ModelState.Remove("Finalize.DepartmentId");
        }

        await LoadEmployeeCodeSchemaAsync();

        if (CodeSchemaActive)
        {
            // Auto numbering owns the code when enabled. Never trust or reserve
            // a posted preview value; the real code is generated atomically
            // inside the finalization transaction.
            Finalize.EmployeeNo = string.Empty;
            ModelState.Remove("Finalize.EmployeeNo");
        }

        NormalizeFinalizeInput();

        if (!ValidateFinalizeInput())
        {
            await LoadAsync(initializeFinalize: false);
            return Page();
        }

        if (!context.Value.Access.Scope.AllowsLocation(
                CompanyId,
                Finalize.BranchId,
                Finalize.DepartmentId))
        {
            TempData["SmartOnboardingReviewError"] =
                "موقع العمل أو القسم خارج نطاق صلاحيتك.";
            return RedirectToSelf();
        }

        var branchValid = (await _employeeService
                .GetBranchesForDropdownAsync(
                    CompanyId,
                    context.Value.Access.Scope))
            .Any(x => x.Id == Finalize.BranchId &&
                      x.CompanyId == CompanyId &&
                      x.IsActive);

        var departmentValid = (await _employeeService
                .GetDepartmentsForDropdownAsync(
                    CompanyId,
                    context.Value.Access.Scope))
            .Any(x => x.Id == Finalize.DepartmentId &&
                      (x.BranchId == 0 ||
                       x.BranchId == Finalize.BranchId) &&
                      x.CompanyId == CompanyId &&
                      x.IsActive);

        if (!branchValid || !departmentValid)
        {
            TempData["SmartOnboardingReviewError"] =
                "الفرع أو القسم غير صالح للشركة المحددة.";
            return RedirectToSelf();
        }

        if (Finalize.DirectManagerId is > 0)
        {
            var managerValid = await _db.Employees
                .AsNoTracking()
                .AnyAsync(x =>
                    x.Id == Finalize.DirectManagerId.Value &&
                    !x.IsDeleted &&
                    x.IsActive &&
                    x.CompanyId == CompanyId,
                    HttpContext.RequestAborted);

            if (!managerValid)
            {
                TempData["SmartOnboardingReviewError"] =
                    "المدير المباشر المحدد غير صالح لهذه الشركة.";
                return RedirectToSelf();
            }
        }

        var requiredDocumentError =
            await ValidateRequiredDocumentsAsync(
                context.Value.Session,
                Finalize.IsCitizen);

        if (requiredDocumentError is not null)
        {
            TempData["SmartOnboardingReviewError"] =
                requiredDocumentError;
            return RedirectToSelf();
        }

        var duplicateError = await ValidateFinalDuplicatesAsync(
            context.Value.Access,
            context.Value.Settings);

        if (duplicateError is not null)
        {
            TempData["SmartOnboardingReviewError"] =
                duplicateError;
            return RedirectToSelf();
        }

        await EmployeeRecordsSchema.EnsureAsync(_db);

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                HttpContext.RequestAborted);

        var promotedFiles =
            new Dictionary<long, PromotedEmployeeFile>();

        try
        {
            var locked =
                await PeopleAiFinalizationStore.TryBeginFinalizationAsync(
                    _db,
                    SessionId,
                    CompanyId);

            if (!locked)
            {
                await transaction.RollbackAsync(
                    HttpContext.RequestAborted);

                var latest =
                    await EmployeeOnboardingStore.GetSessionAsync(
                        _db,
                        SessionId);

                if (latest?.Status == "Completed" &&
                    latest.CreatedEmployeeId is > 0)
                {
                    return RedirectToPage(
                        "/Employees/Profile",
                        new { id = latest.CreatedEmployeeId.Value });
                }

                TempData["SmartOnboardingReviewError"] =
                    "تعذر بدء الإنهاء لأن الجلسة تغيرت. أعد تحميل الصفحة.";
                return RedirectToSelf();
            }

            if (CodeSchemaActive)
            {
                var generatedCode =
                    await EmployeeCodeSchema.GenerateNextAsync(_db);

                if (string.IsNullOrWhiteSpace(generatedCode))
                {
                    await transaction.RollbackAsync(
                        HttpContext.RequestAborted);
                    TempData["SmartOnboardingReviewError"] =
                        "تعذر توليد كود الموظف التلقائي. راجع مخطط رمز الموظف.";
                    return RedirectToSelf();
                }

                Finalize.EmployeeNo = generatedCode;
            }

            var create = BuildCreateViewModel();
            var employeeId =
                await _employeeService.CreateAndGetIdAsync(create);

            if (employeeId is not > 0)
            {
                await transaction.RollbackAsync(
                    HttpContext.RequestAborted);
                TempData["SmartOnboardingReviewError"] =
                    "تعذر إنشاء الموظف. تحقق من كود الموظف والبيانات الوظيفية.";
                return RedirectToSelf();
            }

            if (Finalize.DirectManagerId is > 0)
            {
                await HrmsDatabase.ExecuteAsync(
                    _db,
                    """
UPDATE dbo.Employees
SET DirectManagerId = @DirectManagerId
WHERE Id = @EmployeeId
  AND CompanyId = @CompanyId;
""",
                    command =>
                    {
                        HrmsDatabase.AddParameter(
                            command,
                            "@DirectManagerId",
                            Finalize.DirectManagerId.Value);
                        HrmsDatabase.AddParameter(
                            command,
                            "@EmployeeId",
                            employeeId.Value);
                        HrmsDatabase.AddParameter(
                            command,
                            "@CompanyId",
                            CompanyId);
                    });
            }

            await SaveEmployeeNameTranslationsAsync(
                employeeId.Value);

            await PeopleAiStructuredRecordStore.PromoteAcceptedAsync(
                _db,
                SessionId,
                employeeId.Value,
                User.Identity?.Name ?? "HR");

            var onboardingDocuments =
                await EmployeeOnboardingStore.ListDocumentsAsync(
                    _db,
                    SessionId);

            foreach (var document in onboardingDocuments)
            {
                var category =
                    document.DetectedDocumentType ??
                    document.DeclaredDocumentType ??
                    PeopleAiDocumentTypes.Unknown;

                var promoted =
                    await _protectedAssets.PromoteToEmployeeAsync(
                        document.ProtectedFileAssetId,
                        CompanyId,
                        SessionId,
                        employeeId.Value,
                        category,
                        HttpContext.RequestAborted);

                if (promoted is null)
                {
                    throw new InvalidOperationException(
                        $"تعذر نقل المستند {document.Id} إلى المخزن المحمي للموظف.");
                }

                promotedFiles[document.ProtectedFileAssetId] =
                    promoted;
            }

            await PeopleAiFinalizationStore
                .LinkIdentityDocumentsAndCompleteAsync(
                    _db,
                    SessionId,
                    CompanyId,
                    employeeId.Value,
                    context.Value.Access.SystemUserId,
                    Finalize.NationalId,
                    Finalize.FamilyNumber,
                    Finalize.PassportNo,
                    promotedFiles,
                    User.Identity?.Name ?? "HR");

            await transaction.CommitAsync(
                HttpContext.RequestAborted);

            foreach (var promoted in promotedFiles.Values)
            {
                _protectedAssets.TryDeleteOnboardingSource(
                    promoted.OriginalStorageKey);
            }

            TempData["StatusMessage"] =
                "تم إنشاء الموظف وربط المستندات من Smart Onboarding بنجاح.";

            return RedirectToPage(
                "/Employees/Profile",
                new { id = employeeId.Value });
        }
        catch
        {
            await transaction.RollbackAsync(
                HttpContext.RequestAborted);

            foreach (var promoted in promotedFiles.Values)
            {
                _protectedFiles.TryDelete(
                    promoted.StoredPath);
            }

            throw;
        }
    }

    private async Task LoadAsync(bool initializeFinalize)
    {
        var context = await GetAuthorizedContextAsync(
            requireReviewer: false);

        if (context is null)
        {
            PageError =
                "الجلسة غير موجودة أو لا تملك صلاحية الوصول إليها.";
            return;
        }

        Session = context.Value.Session;
        CanReview = IsReviewerAllowed(
            context.Value.Access,
            context.Value.Settings);
        CanVerifyOriginal =
            await CanVerifyOriginalAsync(context.Value.Access);
        CanCreateEmployee =
            await CanCreateEmployeeAsync(context.Value.Access);

        await LoadEmployeeCodeSchemaAsync();

        Documents = await EmployeeOnboardingStore
            .ListDocumentsAsync(_db, SessionId);

        DocumentPolicies =
            await PeopleAiSettingsStore.ListDocumentPoliciesAsync(
                _db,
                CompanyId);

        FieldPolicies =
            await PeopleAiSettingsStore.ListFieldPoliciesAsync(
                _db,
                CompanyId);

        await PeopleAiReviewStore.EnsureConfiguredFieldsAsync(
            _db,
            CompanyId,
            SessionId);

        Fields = await PeopleAiReviewStore
            .ListLatestFieldsAsync(_db, SessionId);

        StructuredRecords =
            await PeopleAiStructuredRecordStore.ListLatestAsync(
                _db,
                SessionId);

        var inferredCitizen = Documents.Any(document =>
            string.Equals(
                document.DetectedDocumentType ??
                document.DeclaredDocumentType,
                PeopleAiDocumentTypes.NationalId,
                StringComparison.OrdinalIgnoreCase));

        DocumentPolicyError =
            await ValidateRequiredDocumentsAsync(
                context.Value.Session,
                inferredCitizen);

        // Ready represents completed human review.
        // Missing required documents remain finalization prerequisites.
        // Do not reopen completed review merely because Contract is missing.

        await RefreshCrossDocumentIssuesAsync(
            persistIssues: CanReview);

        if (CanReview)
        {
            await RefreshDuplicateIssuesAsync(
                context.Value.Access,
                context.Value.Settings);
        }

        Issues = await PeopleAiReviewStore
            .ListIssuesAsync(_db, SessionId);

        CanMarkReady =
            CanReview &&
            !await PeopleAiStructuredRecordStore.HasPendingLatestAsync(
                _db,
                SessionId) &&
            await PeopleAiReviewStore.CanMarkReadyAsync(
                _db,
                SessionId);

        Branches = (await _employeeService
                .GetBranchesForDropdownAsync(
                    CompanyId,
                    context.Value.Access.Scope))
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToList();

        Departments = (await _employeeService
                .GetDepartmentsForDropdownAsync(
                    CompanyId,
                    context.Value.Access.Scope))
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToList();

        Positions = (await _employeeService
                .GetPositionsForDropdownAsync())
            .Where(x => x.CompanyId == CompanyId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToList();

        ReligionOptions =
            await HrLookups.ValuesAsync(_db, "religions");
        WorkTypeOptions =
            await HrLookups.ValuesAsync(_db, "worktypes");
        GradeOptions =
            await HrLookups.ValuesAsync(_db, "grades");
        SponsorOptions =
            await HrLookups.ValuesAsync(_db, "sponsors");

        ManagerOptions = await _db.Employees
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.IsActive &&
                x.CompanyId == CompanyId)
            .OrderBy(x => x.FullName)
            .Select(x => new ManagerOption(
                x.Id,
                x.EmployeeNo,
                x.FullName))
            .ToListAsync(HttpContext.RequestAborted);

        if (initializeFinalize)
        {
            InitializeFinalizeDefaults();
        }
    }

    private async Task LoadEmployeeCodeSchemaAsync()
    {
        var schema = await EmployeeCodeSchema.GetAsync(_db);
        CodeSchemaActive = schema?.IsActive == true;
        CodeSchemaPreview = schema is { IsActive: true }
            ? schema.Prefix +
              (schema.LastNumber + 1).ToString(
                  new string(
                      '0',
                      Math.Clamp(schema.Digits, 1, 12)))
            : null;
    }

    private async Task<(
        EmployeeOnboardingStore.SessionRow Session,
        PeopleAiAccessContext Access,
        CompanyPeopleAiPolicy Settings)?> GetAuthorizedContextAsync(
        bool requireReviewer)
    {
        if (CompanyId <= 0 || SessionId <= 0)
        {
            return null;
        }

        var access = await _sessionAccess.ResolveAsync(
            HttpContext,
            HttpContext.RequestAborted);

        var session = await EmployeeOnboardingStore
            .GetSessionAsync(_db, SessionId);

        if (!_sessionAccess.CanAccessSession(
                session,
                CompanyId,
                access))
        {
            return null;
        }

        var settings = await PeopleAiSettingsStore
            .GetAsync(_db, CompanyId);

        if (!settings.IsEnabled)
        {
            return null;
        }

        if (requireReviewer &&
            !IsReviewerAllowed(access, settings))
        {
            return null;
        }

        return (session!, access, settings);
    }

    private static bool IsReviewerAllowed(
        PeopleAiAccessContext access,
        CompanyPeopleAiPolicy settings)
    {
        if (access.IsAdmin)
        {
            return true;
        }

        return settings.ReviewerMode ==
               PeopleAiReviewerMode.AdminOrCreatorWithPermission &&
               access.SystemUserId is > 0;
    }

    private async Task<bool> CanVerifyOriginalAsync(
        PeopleAiAccessContext access)
    {
        if (access.IsAdmin)
        {
            return true;
        }

        if (access.SystemUserId is not > 0)
        {
            return false;
        }

        return await _permissionAuthorization
            .HasPermissionAsync(
                access.SystemUserId.Value,
                PeoplePermissionCodes.VerifyOriginalDocument,
                compatibilityAllowed: false,
                HttpContext.RequestAborted);
    }

    private async Task<bool> CanCreateEmployeeAsync(
        PeopleAiAccessContext access)
    {
        if (access.IsAdmin)
        {
            return true;
        }

        if (access.SystemUserId is not > 0)
        {
            return false;
        }

        return await _permissionAuthorization
            .HasPermissionAsync(
                access.SystemUserId.Value,
                PeoplePermissionCodes.Create,
                compatibilityAllowed: false,
                HttpContext.RequestAborted);
    }

    private async Task RefreshCrossDocumentIssuesAsync(
        bool persistIssues)
    {
        Documents = Documents.Count > 0
            ? Documents
            : await EmployeeOnboardingStore
                .ListDocumentsAsync(_db, SessionId);

        Fields = Fields.Count > 0
            ? Fields
            : await PeopleAiReviewStore
                .ListLatestFieldsAsync(_db, SessionId);

        CrossDocumentComparisons = [];

        foreach (var tracked in CrossDocumentTrackedFields)
        {
            var fieldKey = tracked.Key;
            var severity = tracked.Value;
            var ruleCode = CrossDocumentRuleCode(fieldKey);

            var values = Fields
                .Where(field =>
                    field.FieldKey.Equals(
                        fieldKey,
                        StringComparison.OrdinalIgnoreCase))
                .Select(field => new
                {
                    Field = field,
                    Value = EffectiveFieldValue(field),
                    Document = Documents.FirstOrDefault(
                        document => document.Id == field.DocumentId)
                })
                .Where(x =>
                    x.Document is not null &&
                    !string.IsNullOrWhiteSpace(x.Value))
                .GroupBy(x => x.Field.DocumentId)
                .Select(group => group
                    .OrderByDescending(x =>
                        x.Field.ReviewStatus is "Accepted" or "Modified")
                    .ThenByDescending(x => x.Field.ProviderConfidence ?? 0m)
                    .ThenByDescending(x => x.Field.Id)
                    .First())
                .Select(x => new CrossDocumentValue(
                    x.Field.DocumentId,
                    x.Document!.DetectedDocumentType ??
                        x.Document.DeclaredDocumentType ??
                        PeopleAiDocumentTypes.Unknown,
                    x.Document.OriginalFileName,
                    x.Value!,
                    x.Field.ProviderConfidence,
                    x.Field.ReviewStatus))
                .ToList();

            if (values.Count < 2)
            {
                if (persistIssues)
                {
                    await PeopleAiReviewStore.ResolveRuleAutomaticallyAsync(
                        _db,
                        SessionId,
                        ruleCode,
                        "لا توجد قيم متعددة كافية للمقارنة بين المستندات.");
                }
                continue;
            }

            var distinctValues = values
                .Select(value =>
                    NormalizeCrossDocumentValue(
                        fieldKey,
                        value.Value))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var hasConflict = distinctValues.Count > 1;
            CrossDocumentComparisons.Add(
                new CrossDocumentComparison(
                    fieldKey,
                    severity,
                    hasConflict,
                    values));

            if (!persistIssues)
            {
                continue;
            }

            if (hasConflict)
            {
                await PeopleAiReviewStore.UpsertDynamicIssueAsync(
                    _db,
                    SessionId,
                    ruleCode,
                    "CrossDocument",
                    severity,
                    fieldKey,
                    $"القيم المستخرجة للحقل {fieldKey} تختلف بين {values.Count} مستندات. يلزم التحقق البشري.");
            }
            else
            {
                await PeopleAiReviewStore.ResolveRuleAutomaticallyAsync(
                    _db,
                    SessionId,
                    ruleCode,
                    "تطابقت القيم بين المستندات بعد المراجعة.");
            }
        }
    }

    private async Task RefreshDuplicateIssuesAsync(
        PeopleAiAccessContext access,
        CompanyPeopleAiPolicy settings)
    {
        Documents = Documents.Count > 0
            ? Documents
            : await EmployeeOnboardingStore
                .ListDocumentsAsync(_db, SessionId);

        Fields = Fields.Count > 0
            ? Fields
            : await PeopleAiReviewStore
                .ListLatestFieldsAsync(_db, SessionId);

        DuplicateMatches = [];

        foreach (var document in Documents)
        {
            var type = document.DetectedDocumentType ??
                       document.DeclaredDocumentType ??
                       PeopleAiDocumentTypes.Unknown;

            if (!IsDuplicateTrackedType(type))
            {
                continue;
            }

            var field = Fields
                .Where(x =>
                    x.DocumentId == document.Id &&
                    x.FieldKey.Equals(
                        "DocumentNumber",
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();

            var value = EffectiveFieldValue(field);
            var normalized =
                IdentityDocumentNormalizer.NormalizeNumber(value);

            if (normalized.Length == 0)
            {
                continue;
            }

            var result = await PeopleIdentityDuplicateStore.FindAsync(
                _db,
                access.Scope,
                CompanyId,
                type,
                normalized);

            var baseRuleCode = DuplicateRuleCode(type, normalized);
            var reviewRuleCode = "DUPLICATE_REVIEW_" + baseRuleCode;
            var blockRuleCode = "DUPLICATE_BLOCK_" + baseRuleCode;

            if (!result.HasDuplicate)
            {
                await PeopleAiReviewStore.ResolveRuleAutomaticallyAsync(
                    _db,
                    SessionId,
                    reviewRuleCode,
                    "تم إغلاق المطابقة تلقائياً بعد تغير القيمة أو زوال المطابقة.");
                await PeopleAiReviewStore.ResolveRuleAutomaticallyAsync(
                    _db,
                    SessionId,
                    blockRuleCode,
                    "تم إغلاق المطابقة تلقائياً بعد تغير القيمة أو زوال المطابقة.");
                continue;
            }

            var actualRuleCode =
                result.Action == PeopleAiDuplicateAction.BlockCreation
                    ? blockRuleCode
                    : reviewRuleCode;

            if (result.Action == PeopleAiDuplicateAction.BlockCreation)
            {
                await PeopleAiReviewStore.ResolveRuleAutomaticallyAsync(
                    _db,
                    SessionId,
                    reviewRuleCode,
                    "تم استبدالها بسياسة BlockCreation الحالية.");
            }
            else
            {
                await PeopleAiReviewStore.ResolveRuleAutomaticallyAsync(
                    _db,
                    SessionId,
                    blockRuleCode,
                    "لم تعد سياسة الشركة تمنع الإنشاء لهذه المطابقة.");
            }

            DuplicateMatches.Add(new DuplicateView(
                document.Id,
                type,
                actualRuleCode,
                normalized,
                result.Action,
                result.Candidates));

            var hasVisibleInactive =
                result.Candidates.Any(x =>
                    x.IsVisibleToRequester && !x.IsActive);

            var severity =
                result.Action == PeopleAiDuplicateAction.WarnOnly
                    ? "Warning"
                    : "Blocking";

            var message = hasVisibleInactive
                ? "يوجد موظف سابق مطابق ضمن نطاق صلاحيتك. لا تنشئ سجلاً جديداً؛ راجع مسار إعادة التعيين."
                : result.Candidates.Any(x => !x.IsVisibleToRequester)
                    ? "توجد مطابقة ضمن نطاق أوسع لا تملك صلاحية عرض تفاصيله."
                    : $"تم العثور على {result.Candidates.Count} مطابقة لنفس رقم المستند.";

            await PeopleAiReviewStore.EnsureIssueAsync(
                _db,
                SessionId,
                document.Id,
                actualRuleCode,
                "Duplicate",
                severity,
                "DocumentNumber",
                message);
        }
    }

    private async Task<string?> ValidateFinalDuplicatesAsync(
        PeopleAiAccessContext access,
        CompanyPeopleAiPolicy settings)
    {
        var checks = new[]
        {
            (PeopleAiDocumentTypes.NationalId, Finalize.NationalId),
            (PeopleAiDocumentTypes.Passport, Finalize.PassportNo)
        };

        foreach (var (type, value) in checks)
        {
            var normalized =
                IdentityDocumentNormalizer.NormalizeNumber(value);

            if (normalized.Length == 0)
            {
                continue;
            }

            var result = await PeopleIdentityDuplicateStore.FindAsync(
                _db,
                access.Scope,
                CompanyId,
                type,
                normalized);

            if (!result.HasDuplicate)
            {
                continue;
            }

            var visibleInactive = result.Candidates
                .FirstOrDefault(x =>
                    x.IsVisibleToRequester && !x.IsActive);

            if (visibleInactive is not null)
            {
                return
                    $"يوجد موظف سابق مطابق ({visibleInactive.EmployeeNo} - {visibleInactive.DisplayName}). استخدم إعادة التعيين بدلاً من إنشاء موظف مكرر.";
            }

            if (result.Action ==
                PeopleAiDuplicateAction.BlockCreation)
            {
                return
                    $"سياسة الشركة تمنع الإنشاء لأن {type} مطابق لسجل موجود.";
            }

            if (result.Action ==
                PeopleAiDuplicateAction.RequireReview)
            {
                var ruleCode =
                    "DUPLICATE_REVIEW_" +
                    DuplicateRuleCode(type, normalized);

                if (!await PeopleAiFinalizationStore
                        .HasResolvedIssueAsync(
                            _db,
                            SessionId,
                            ruleCode))
                {
                    return
                        $"مطابقة {type} تحتاج قرار مراجعة موثق قبل الإنشاء.";
                }
            }
        }

        return null;
    }

    private async Task<string?> ValidateRequiredDocumentsAsync(
        EmployeeOnboardingStore.SessionRow session,
        bool isCitizen)
    {
        var policies =
            await PeopleAiSettingsStore.ListDocumentPoliciesAsync(
                _db,
                session.CompanyId);

        var documents =
            await EmployeeOnboardingStore.ListDocumentsAsync(
                _db,
                SessionId);

        var fields =
            await PeopleAiReviewStore.ListLatestFieldsAsync(
                _db,
                SessionId);

        var category = isCitizen ? "Citizen" : "Expat";
        var applicable = policies
            .Where(x =>
                x.IsActive &&
                (x.EmployeeCategory == "All" ||
                 x.EmployeeCategory == category))
            .GroupBy(
                x => x.DocumentType,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                DocumentType = group.Key,
                IsRequired = group.Any(x => x.Requirement == "Required"),
                IsApplicable = group.Any(x => x.Requirement != "NotApplicable"),
                RequireExpiryDate = group.Any(x => x.RequireExpiryDate),
                RequireOriginalVerification =
                    group.Any(x => x.RequireOriginalVerification)
            })
            .Where(x => x.IsApplicable)
            .ToList();

        foreach (var policy in applicable)
        {
            var matching = documents
                .Where(x => string.Equals(
                    x.DetectedDocumentType ??
                    x.DeclaredDocumentType,
                    policy.DocumentType,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (policy.IsRequired && matching.Count == 0)
            {
                return
                    $"المستند المطلوب غير موجود: {policy.DocumentType}.";
            }

            if (matching.Count == 0)
            {
                continue;
            }

            if (matching.Any(x =>
                    x.ProcessingStatus is "Queued" or "Processing"))
            {
                return
                    $"معالجة {policy.DocumentType} لم تكتمل بعد.";
            }

            if (matching.All(x => x.ProcessingStatus == "Failed"))
            {
                return
                    $"معالجة {policy.DocumentType} فشلت. أعد المعالجة قبل إنشاء الموظف.";
            }

            var today = DateOnly.FromDateTime(DateTime.Today);
            var expiries = matching
                .Select(document =>
                    ResolveDocumentExpiry(document, fields))
                .ToList();

            if (policy.RequireExpiryDate &&
                expiries.All(x => x is null))
            {
                return
                    $"سياسة الشركة تشترط تاريخ انتهاء للمستند {policy.DocumentType}.";
            }

            if (expiries.Any(x => x.HasValue && x.Value < today))
            {
                return
                    $"المستند {policy.DocumentType} منتهي الصلاحية.";
            }

            if (policy.RequireOriginalVerification &&
                !matching.Any(x =>
                    x.OriginalVerificationStatus == "Verified"))
            {
                return
                    $"سياسة الشركة تشترط التحقق من أصل المستند {policy.DocumentType}.";
            }

            if (matching.Any(x =>
                    x.OriginalVerificationStatus == "Rejected"))
            {
                return
                    $"تم رفض مطابقة أصل المستند {policy.DocumentType}.";
            }
        }

        return null;
    }

    private static DateOnly? ResolveDocumentExpiry(
        EmployeeOnboardingStore.DocumentRow document,
        IReadOnlyCollection<PeopleAiReviewStore.ReviewField> fields)
    {
        if (document.ReviewedExpiryDate.HasValue)
        {
            return document.ReviewedExpiryDate;
        }

        var expiry = fields
            .Where(x =>
                x.DocumentId == document.Id &&
                x.FieldKey.Equals(
                    "ExpiryDate",
                    StringComparison.OrdinalIgnoreCase) &&
                x.ReviewStatus is "Accepted" or "Modified")
            .OrderByDescending(x => x.Id)
            .Select(EffectiveFieldValue)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        return DateOnly.TryParse(expiry, out var parsed)
            ? parsed
            : null;
    }

    private void InitializeFinalizeDefaults()
    {
        bool IsActiveConfiguredField(
            PeopleAiReviewStore.ReviewField field)
        {
            var document = Documents.FirstOrDefault(
                x => x.Id == field.DocumentId);
            var documentType =
                document?.DetectedDocumentType ??
                document?.DeclaredDocumentType ??
                PeopleAiDocumentTypes.Unknown;

            return FieldPolicies.Any(policy =>
                policy.IsActive &&
                policy.DocumentType.Equals(
                    documentType,
                    StringComparison.OrdinalIgnoreCase) &&
                policy.FieldKey.Equals(
                    field.FieldKey,
                    StringComparison.OrdinalIgnoreCase));
        }

        var semantic = Fields
            .Where(x =>
                !x.FieldKey.StartsWith(
                    "OCR.",
                    StringComparison.OrdinalIgnoreCase) &&
                !x.FieldKey.StartsWith(
                    "MRZ.Check.",
                    StringComparison.OrdinalIgnoreCase) &&
                x.ReviewStatus is "Accepted" or "Modified" &&
                IsActiveConfiguredField(x))
            .ToList();

        string? Get(string key) =>
            semantic
                .Where(x => x.FieldKey.Equals(
                    key,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Id)
                .Select(EffectiveFieldValue)
                .FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x));

        var firstName = Get("FirstName");
        var secondName = Get("SecondName");
        var thirdName = Get("ThirdName");
        var lastName = Get("LastName");

        var givenNamesEn = (Get("GivenNames") ?? string.Empty)
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        var surnameEn = Get("Surname");
        var documentNumber = Get("DocumentNumber");

        var passportDocIds = Documents
            .Where(x =>
                string.Equals(
                    x.DetectedDocumentType ??
                    x.DeclaredDocumentType,
                    PeopleAiDocumentTypes.Passport,
                    StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Id)
            .ToHashSet();

        var passportNumber = semantic
            .Where(x =>
                passportDocIds.Contains(x.DocumentId) &&
                x.FieldKey.Equals(
                    "DocumentNumber",
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Id)
            .Select(EffectiveFieldValue)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        var nationalDocIds = Documents
            .Where(x =>
                string.Equals(
                    x.DetectedDocumentType ??
                    x.DeclaredDocumentType,
                    PeopleAiDocumentTypes.NationalId,
                    StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Id)
            .ToHashSet();

        var nationalNumber = semantic
            .Where(x =>
                nationalDocIds.Contains(x.DocumentId) &&
                x.FieldKey.Equals(
                    "NationalNumber",
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Id)
            .Select(EffectiveFieldValue)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        var nationalDocumentNumber = semantic
            .Where(x =>
                nationalDocIds.Contains(x.DocumentId) &&
                x.FieldKey.Equals(
                    "DocumentNumber",
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Id)
            .Select(EffectiveFieldValue)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

        var rawNationality = Get("Nationality");
        var rawIssuingCountry = Get("IssuingCountry");

        var mappedCountry =
            ZynoraEmployeeLookups.NormalizePrimaryCountry(
                rawIssuingCountry ?? rawNationality);
        var mappedNationality =
            ZynoraEmployeeLookups.NormalizePrimaryNationality(
                rawNationality ?? rawIssuingCountry);

        var arabicFullName = string.Join(
            ' ',
            new[] { firstName, secondName, thirdName, lastName }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        var englishFullName = string.Join(
            ' ',
            givenNamesEn.Append(surnameEn)
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        Finalize = new FinalizeEmployeeInput
        {
            FullName = !string.IsNullOrWhiteSpace(arabicFullName)
                ? arabicFullName
                : !string.IsNullOrWhiteSpace(englishFullName)
                    ? englishFullName
                    : Get("FullName") ?? string.Empty,
            FirstName = firstName,
            SecondName = secondName,
            ThirdName = thirdName,
            LastName = lastName,
            FirstNameEn = givenNamesEn.ElementAtOrDefault(0),
            SecondNameEn = givenNamesEn.ElementAtOrDefault(1),
            ThirdNameEn = givenNamesEn.ElementAtOrDefault(2),
            LastNameEn = surnameEn,
            BirthDate = DateOnly.TryParse(
                Get("DateOfBirth"),
                out var birth)
                    ? birth
                    : null,
            Nationality = mappedNationality,
            Country = mappedCountry,
            Gender = NormalizeGender(Get("Sex")),
            MaritalStatus = Get("MaritalStatus"),
            Religion = Get("Religion"),
            SponsorName = Get("SponsorName"),
            MotherCountry = Get("MotherCountry"),
            MotherCity = Get("MotherCity"),
            Phone = Get("Phone"),
            PhoneExtension = Get("PhoneExtension"),
            Email = Get("Email"),
            PersonalEmail = Get("PersonalEmail"),
            WorkType = Get("WorkType"),
            JobGrade = Get("JobGrade"),
            PassportNo =
                passportNumber ??
                (passportDocIds.Count > 0
                    ? documentNumber
                    : null),
            NationalId = nationalNumber ?? nationalDocumentNumber,
            FamilyNumber = Get("FamilyNumber"),
            IsCitizen = nationalDocIds.Count > 0,
            IsActive = true,
            HireDate = DateOnly.FromDateTime(DateTime.Today),
            JoiningDate = DateOnly.FromDateTime(DateTime.Today),
            BranchId = Branches.FirstOrDefault()?.Id ?? 0,
            DepartmentId = 0
        };

        if (Finalize.BranchId > 0)
        {
            Finalize.DepartmentId = Departments
                .FirstOrDefault(x =>
                    x.BranchId == Finalize.BranchId)?.Id ?? 0;
        }
    }

    private async Task SaveEmployeeNameTranslationsAsync(
        int employeeId)
    {
        var languages = await _dataLocalization.GetLanguagesAsync(
            CompanyId,
            HttpContext.RequestAborted);

        var values = new List<LocalizedFieldValue>();

        foreach (var language in languages)
        {
            if (language.CultureCode.StartsWith(
                    "ar",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddLocalizedNameValues(
                    values,
                    language.CultureCode,
                    Finalize.FirstName,
                    Finalize.SecondName,
                    Finalize.ThirdName,
                    Finalize.LastName);
                continue;
            }

            if (language.CultureCode.StartsWith(
                    "en",
                    StringComparison.OrdinalIgnoreCase))
            {
                AddLocalizedNameValues(
                    values,
                    language.CultureCode,
                    Finalize.FirstNameEn,
                    Finalize.SecondNameEn,
                    Finalize.ThirdNameEn,
                    Finalize.LastNameEn);
            }
        }

        if (values.Count == 0)
        {
            return;
        }

        await _dataLocalization.SaveValuesAsync(
            CompanyId,
            "Employee",
            employeeId,
            values,
            HttpContext.RequestAborted);
    }

    private static void AddLocalizedNameValues(
        ICollection<LocalizedFieldValue> values,
        string cultureCode,
        string? firstName,
        string? secondName,
        string? thirdName,
        string? lastName)
    {
        firstName = Clean(firstName);
        secondName = Clean(secondName);
        thirdName = Clean(thirdName);
        lastName = Clean(lastName);

        values.Add(new(
            cultureCode,
            "FirstName",
            firstName));
        values.Add(new(
            cultureCode,
            "SecondName",
            secondName));
        values.Add(new(
            cultureCode,
            "ThirdName",
            thirdName));
        values.Add(new(
            cultureCode,
            "LastName",
            lastName));

        values.Add(new(
            cultureCode,
            "FullName",
            string.Join(
                ' ',
                new[] {
                    firstName,
                    secondName,
                    thirdName,
                    lastName
                }
                .Where(x => !string.IsNullOrWhiteSpace(x)))));
    }

    private EmployeeCreateViewModel BuildCreateViewModel() =>
        new()
        {
            EmployeeNo = Finalize.EmployeeNo.Trim(),
            FullName = Finalize.FullName.Trim(),
            FirstName = Clean(Finalize.FirstName),
            SecondName = Clean(Finalize.SecondName),
            ThirdName = Clean(Finalize.ThirdName),
            LastName = Clean(Finalize.LastName),
            FirstNameEn = Clean(Finalize.FirstNameEn),
            SecondNameEn = Clean(Finalize.SecondNameEn),
            ThirdNameEn = Clean(Finalize.ThirdNameEn),
            LastNameEn = Clean(Finalize.LastNameEn),
            IsCitizen = Finalize.IsCitizen,
            NationalId = Clean(Finalize.NationalId),
            PassportNo = Clean(Finalize.PassportNo),
            SponsorName = Clean(Finalize.SponsorName),
            Religion = Clean(Finalize.Religion),
            PersonalEmail = Clean(Finalize.PersonalEmail),
            MotherCountry = Clean(Finalize.MotherCountry),
            MotherCity = Clean(Finalize.MotherCity),
            PhoneExtension = Clean(Finalize.PhoneExtension),
            BirthDate = Finalize.BirthDate,
            Nationality = Clean(Finalize.Nationality),
            Country = Clean(Finalize.Country),
            Gender = Clean(Finalize.Gender),
            MaritalStatus = Clean(Finalize.MaritalStatus),
            Phone = Clean(Finalize.Phone),
            Email = Clean(Finalize.Email),
            HireDate = Finalize.HireDate,
            JoiningDate = Finalize.JoiningDate,
            WorkType = Clean(Finalize.WorkType),
            JobGrade = Clean(Finalize.JobGrade),
            BranchId = Finalize.BranchId,
            DepartmentId = Finalize.DepartmentId,
            PositionId = Finalize.PositionId,
            IsActive = Finalize.IsActive
        };

    private bool ValidateFinalizeInput()
    {
        if (!CodeSchemaActive &&
            string.IsNullOrWhiteSpace(Finalize.EmployeeNo))
        {
            ModelState.AddModelError(
                "Finalize.EmployeeNo",
                "كود الموظف مطلوب عند إيقاف الترقيم التلقائي.");
        }

        if (string.IsNullOrWhiteSpace(Finalize.FullName))
        {
            ModelState.AddModelError(
                "Finalize.FullName",
                "اسم الموظف مطلوب.");
        }

        if (Finalize.BranchId <= 0)
        {
            ModelState.AddModelError(
                "Finalize.BranchId",
                "اختر موقع العمل.");
        }

        if (Finalize.DepartmentId <= 0)
        {
            ModelState.AddModelError(
                "Finalize.DepartmentId",
                "اختر القسم.");
        }

        if (Finalize.IsCitizen &&
            string.IsNullOrWhiteSpace(Finalize.NationalId))
        {
            ModelState.AddModelError(
                "Finalize.NationalId",
                "الرقم الوطني مطلوب للموظف المواطن.");
        }

        if (!Finalize.IsCitizen &&
            string.IsNullOrWhiteSpace(Finalize.PassportNo))
        {
            ModelState.AddModelError(
                "Finalize.PassportNo",
                "رقم الجواز مطلوب للموظف الوافد.");
        }

        return ModelState.IsValid;
    }

    private void NormalizeFinalizeInput()
    {
        Finalize.EmployeeNo =
            Finalize.EmployeeNo?.Trim() ?? string.Empty;
        Finalize.FullName =
            Finalize.FullName?.Trim() ?? string.Empty;
        Finalize.NationalId =
            Clean(Finalize.NationalId);
        Finalize.FamilyNumber =
            Clean(Finalize.FamilyNumber);
        Finalize.PassportNo =
            Clean(Finalize.PassportNo);
        Finalize.Country =
            ZynoraEmployeeLookups.NormalizePrimaryCountry(
                Finalize.Country);
        Finalize.Nationality =
            ZynoraEmployeeLookups.NormalizePrimaryNationality(
                Finalize.Nationality);
    }

    private IActionResult RedirectToSelf() =>
        RedirectToPage(new
        {
            CompanyId,
            SessionId
        });

    private static bool IsDuplicateTrackedType(string type) =>
        type.Equals(
            PeopleAiDocumentTypes.NationalId,
            StringComparison.OrdinalIgnoreCase) ||
        type.Equals(
            PeopleAiDocumentTypes.Passport,
            StringComparison.OrdinalIgnoreCase);

    private static string DuplicateRuleCode(
        string type,
        string normalizedNumber)
    {
        var safeType = new string(
            type.ToUpperInvariant()
                .Where(char.IsLetterOrDigit)
                .Take(24)
                .ToArray());

        var safeNumber = new string(
            normalizedNumber.ToUpperInvariant()
                .Where(char.IsLetterOrDigit)
                .Take(48)
                .ToArray());

        return $"{safeType}_{safeNumber}";
    }

    private static string CrossDocumentRuleCode(string fieldKey)
    {
        var safe = new string(fieldKey
            .Where(char.IsLetterOrDigit)
            .ToArray())
            .ToUpperInvariant();

        return "CROSS_DOCUMENT_" + safe;
    }

    private static string NormalizeCrossDocumentValue(
        string fieldKey,
        string value)
    {
        var cleaned = Clean(value) ?? string.Empty;
        if (cleaned.Length == 0)
        {
            return string.Empty;
        }

        if (fieldKey.Equals(
                "DateOfBirth",
                StringComparison.OrdinalIgnoreCase) &&
            DateOnly.TryParse(cleaned, out var date))
        {
            return date.ToString("yyyy-MM-dd");
        }

        if (fieldKey.Equals(
                "NationalNumber",
                StringComparison.OrdinalIgnoreCase) ||
            fieldKey.Equals(
                "FamilyNumber",
                StringComparison.OrdinalIgnoreCase))
        {
            return new string(cleaned
                .Where(char.IsLetterOrDigit)
                .ToArray())
                .ToUpperInvariant();
        }

        return string.Join(
                ' ',
                cleaned.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
    }

    private static string? EffectiveFieldValue(
        PeopleAiReviewStore.ReviewField? field)
    {
        if (field is null ||
            field.ReviewStatus == "Rejected")
        {
            return null;
        }

        return Clean(field.ReviewedValue) ??
               Clean(field.NormalizedValue) ??
               Clean(field.RawValue);
    }

    private static string? NormalizeGender(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "M" => "Male",
            "F" => "Female",
            "X" => "Unspecified",
            _ => Clean(value)
        };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
