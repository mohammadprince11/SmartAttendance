using Xunit;

namespace SmartAttendance.Tests;

public sealed class PeopleAiSurfaceTests
{
    [Fact]
    public void PeopleAiSettings_IsAdminOnlyAndLocalOnly()
    {
        var root = FindRoot();
        var model = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "HrSettings",
            "PeopleAI", "Index.cshtml.cs"));
        var page = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "HrSettings",
            "PeopleAI", "Index.cshtml"));

        Assert.Contains("[Authorize(Roles = RoleRouteCatalog.Admin)]", model);
        Assert.Contains("CloudProcessingAllowed: false", model);
        Assert.Contains("Local Only", page);
        Assert.Contains("DuplicateScope", page);
        Assert.Contains("SaveDocumentType", page);
        Assert.Contains("DeleteDocumentType", page);
        Assert.Contains("أنواع المستندات", page);
        Assert.Contains("SaveDocumentPolicy", page);
        Assert.Contains("SaveFieldPolicy", page);
        Assert.Contains("DeleteFieldPolicy", page);
        Assert.Contains("حقول المراجعة", page);
        Assert.Contains("FamilyNumber", page);
    }

    [Fact]
    public void EmployeeCreate_ShowsCoreIdentityBeforePhoto()
    {
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "Create.cshtml"));

        var coreIndex = page.IndexOf(
            "<h2>البيانات الأساسية</h2>",
            StringComparison.Ordinal);
        var photoIndex = page.IndexOf(
            "<h2>صورة الموظف</h2>",
            StringComparison.Ordinal);
        var multilingualIndex = page.IndexOf(
            "data-employee-multilingual",
            StringComparison.Ordinal);

        Assert.True(coreIndex >= 0);
        Assert.True(multilingualIndex > coreIndex);
        Assert.True(photoIndex > coreIndex);
        Assert.True(photoIndex > multilingualIndex);
    }

    [Fact]
    public void SmartOnboarding_ReviewSupportsBulkApprovalAndScrollRestore()
    {
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "SmartOnboardingReview.cshtml"));
        var model = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "SmartOnboardingReview.cshtml.cs"));
        var script = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "wwwroot", "js",
            "smart-onboarding-review.js"));
        var store = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Hrms",
            "PeopleAiReviewStore.cs"));
        var finalizationStore = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Hrms",
            "PeopleAiFinalizationStore.cs"));

        Assert.Contains("asp-page-handler=\"AcceptAll\"", page);
        Assert.Contains("اعتماد عالي الثقة", page);
        Assert.Contains("data-preserve-scroll", page);
        Assert.Contains("OnPostAcceptAllAsync", model);
        Assert.Contains(
            "AcceptHighConfidenceConfiguredFieldsAsync",
            store);
        Assert.Contains(
            "f.ProviderConfidence >= @HighConfidenceThreshold",
            store);
        Assert.Contains("HighConfidenceThreshold = 0.90m", store);
        Assert.Contains("CompanyPeopleAiFieldPolicies", store);
        Assert.Contains("sessionStorage.setItem", script);
        Assert.Contains("window.scrollTo", script);
        Assert.Contains("history.scrollRestoration = \"manual\"", script);
        Assert.Contains("EmployeeCodeSchema.GenerateNextAsync", model);
        Assert.Contains("LoadEmployeeCodeSchemaAsync", model);
        Assert.Contains("ICompanyDataLocalizationService", model);
        Assert.Contains("SaveEmployeeNameTranslationsAsync", model);
        Assert.Contains("_dataLocalization.SaveValuesAsync", model);
        Assert.Contains("Finalize.MaritalStatus", page);
        Assert.Contains("Finalize.Religion", page);
        Assert.Contains("Finalize.MotherCountry", page);
        Assert.Contains("Finalize.MotherCity", page);
        Assert.Contains("Finalize.SponsorName", page);
        Assert.Contains("Finalize.JoiningDate", page);
        Assert.Contains("Finalize.WorkType", page);
        Assert.Contains("Finalize.JobGrade", page);
        Assert.Contains("Finalize.DirectManagerId", page);
        Assert.Contains("Finalize.Phone", page);
        Assert.Contains("Finalize.Email", page);
        Assert.Contains("Finalize.PersonalEmail", page);
        Assert.Contains("Finalize.FamilyNumber", page);
        Assert.Contains("FamilyNumber = Get(\"FamilyNumber\")", model);
        Assert.Contains("Finalize.FamilyNumber", model);
        Assert.Contains("familyNumberOverride", finalizationStore);
        Assert.Contains("reviewFields = semanticFields", page);
        Assert.Contains("acceptedFields = semanticFields", page);
        Assert.Contains("documentsToReview = Model.Documents", page);
        Assert.Contains("reviewedDocuments = Model.Documents", page);
        Assert.Contains("المستندات المعتمدة", page);
        Assert.Contains("الحقول المعتمدة", page);
        Assert.Contains("@foreach (var field in reviewFields)", page);
        Assert.DoesNotContain("@foreach (var field in semanticFields)", page);
        Assert.Contains("DirectManagerId = @DirectManagerId", model);
        Assert.Contains("CodeSchemaPreview", model);
        Assert.Contains("كود الموظف — تلقائي", page);
        Assert.Contains("DocumentPolicyError", model);
        Assert.Contains("ReopenForReviewAsync", model);
        Assert.Contains("ReopenForReviewAsync", store);
        Assert.Contains("متطلب قبل الإنشاء", page);
    }

    [Fact]
    public void SmartOnboarding_UsesProtectedAssetsAndDurableQueue()
    {
        var root = FindRoot();
        var model = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "SmartOnboarding.cshtml.cs"));
        var store = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Hrms",
            "EmployeeOnboardingStore.cs"));
        var protectedAssets = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Security",
            "OnboardingProtectedAssetService.cs"));
        var page = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "SmartOnboarding.cshtml"));
        var script = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "wwwroot", "js",
            "smart-onboarding-live-queue.js"));

        Assert.Contains("IOnboardingProtectedAssetService", model);
        Assert.Contains("DocumentProcessingContract.Resolve", model);
        Assert.Contains("AddDocumentAsync", model);
        Assert.Contains("ShouldQueueAutomaticExtraction", model);
        Assert.Contains("ProcessingStatus = 'Stored'", store);
        Assert.Contains("IdempotencyKey", store);
        Assert.Contains("UPDLOCK, READPAST, ROWLOCK", store);
        Assert.Contains("sp_getapplock", store);
        Assert.Contains("WHERE Status = 'Processing'", store);
        Assert.Contains("AttemptCount >= MaxAttempts", store);
        Assert.Contains("SHA256.Create()", protectedAssets);
        Assert.Contains("UploadSignatureValidator", protectedAssets);
        Assert.Contains("FileThreatPolicy.ScanUploadAsync", protectedAssets);
        Assert.DoesNotContain("wwwroot", protectedAssets, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ListDocumentTypesAsync", model);
        Assert.Contains("data-ajax-upload", page);
        Assert.Contains("data-document-type", page);
        Assert.Contains("ZynoraAjaxUpload", script);
        Assert.Contains("fileInput.value = \"\"", script);
        Assert.Contains("typeSelect.value = selectedType", script);
    }

    [Fact]
    public void SmartOnboarding_RouteRequiresPeopleAiPermission()
    {
        var root = FindRoot();
        var resolver = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Security",
            "PeopleRoutePermissionResolver.cs"));

        Assert.Contains("/employees/smartonboarding", resolver);
        Assert.Contains("PeoplePermissionCodes.AiProcessDocuments", resolver);
    }

    [Fact]
    public void SmartOnboarding_PreservesExplicitAdminBypass()
    {
        var root = FindRoot();
        var middleware = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Security",
            "RoleSecurityMiddleware.cs"));

        Assert.Contains(
            "the authenticated Admin role's unrestricted system access",
            middleware);
        Assert.Contains("if (RoleRouteCatalog.IsAdmin(role))", middleware);
    }

    [Fact]
    public void PdfWorker_IsPackagedWithHybridExtractionGuards()
    {
        var root = FindRoot();
        var worker = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "PeopleAI",
            "local_ocr_worker.py"));
        var requirements = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "PeopleAI",
            "requirements-ocr.txt"));

        Assert.Contains("process_pdf_document", worker);
        Assert.Contains("PDF_PAGE_LIMIT_EXCEEDED", worker);
        Assert.Contains("PDF_PASSWORD_PROTECTED", worker);
        Assert.Contains("process_docx_document", worker);
        Assert.Contains("DOCX_UNCOMPRESSED_LIMIT_EXCEEDED", worker);
        Assert.Contains("DOCX_MACRO_CONTENT_UNSUPPORTED", worker);
        Assert.Contains("DOCX_EMBEDDED_OBJECT_UNSUPPORTED", worker);
        Assert.Contains("process_xlsx_document", worker);
        Assert.Contains("XLSX_CELL_LIMIT_EXCEEDED", worker);
        Assert.Contains("XLSX_MACRO_CONTENT_UNSUPPORTED", worker);
        Assert.Contains("XLSX_EMBEDDED_OBJECT_UNSUPPORTED", worker);
        Assert.Contains("process_legacy_office_document", worker);
        Assert.Contains("LEGACY_OFFICE_MACRO_CONTENT_UNSUPPORTED", worker);
        Assert.Contains("LibreOffice+OpenXML", worker);
        Assert.Contains("PP-OCRv5_mobile_det", worker);
        Assert.Contains("arabic_PP-OCRv5_mobile_rec", worker);
        Assert.Contains("en_PP-OCRv5_mobile_rec", worker);
        Assert.Contains("pypdfium2", requirements);
        Assert.Contains("olefile", requirements);
    }

    [Fact]
    public void ProtectedPreview_IsSessionAndCompanyScoped()
    {
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "SmartOnboarding.cshtml.cs"));
        var markup = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "SmartOnboarding.cshtml"));
        var script = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "wwwroot", "js",
            "smart-onboarding-live-queue.js"));

        Assert.Contains("OnGetPreviewAsync", page);
        Assert.Contains("a.CompanyId = s.CompanyId", page);
        Assert.Contains("a.OwnerType = 'OnboardingSession'", page);
        Assert.Contains("a.OwnerId = s.Id", page);
        Assert.Contains("d.SessionId = @SessionId", page);
        Assert.Contains("s.CompanyId = @CompanyId", page);
        Assert.Contains("Cache-Control", page);
        Assert.Contains("no-store", page);
        Assert.Contains("X-Content-Type-Options", page);
        Assert.Contains("data-preview-open", markup);
        Assert.Contains("data-preview-dialog", markup);
        Assert.Contains("initPreviewDialog", script);
    }

    [Fact]
    public void QueueTelemetry_UsesCurrentMobileOcrModelPerCompany()
    {
        var root = FindRoot();
        var store = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Hrms",
            "EmployeeOnboardingStore.cs"));

        Assert.Contains("JOIN dbo.EmployeeOnboardingSessions s", store);
        Assert.Contains("a.CompanyId = s.CompanyId", store);
        Assert.Contains("log.CompanyId = s.CompanyId", store);
        Assert.Contains("log.Provider = 'PaddleOCR'", store);
        Assert.Contains("log.Model = 'PP-OCRv5-Mobile'", store);
    }

    [Fact]
    public void SmartReview_UsesConfidenceReconciliationAndProtectedPreview()
    {
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "SmartOnboardingReview.cshtml"));
        var model = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "SmartOnboardingReview.cshtml.cs"));
        var workerService = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "PeopleAi",
            "PeopleAiJobProcessorService.cs"));
        var store = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Hrms",
            "PeopleAiReviewStore.cs"));
        var script = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "wwwroot", "js",
            "smart-onboarding-review.js"));

        Assert.Contains("sor-confidence--@confidenceBand", page);
        Assert.Contains("CrossDocumentComparisons", page);
        Assert.Contains("مقارنة البيانات بين المستندات", page);
        Assert.Contains("RefreshCrossDocumentIssuesAsync", model);
        Assert.Contains("UpsertDynamicIssueAsync", store);
        Assert.Contains("CROSS_DOCUMENT_", model);
        Assert.Contains("ResolveObservedConfidence", workerService);
        Assert.Contains("data-review-preview-open", page);
        Assert.Contains("data-review-preview-dialog", page);
        Assert.Contains("data-review-preview-frame", script);
    }

    [Fact]
    public void EmployeeProfile_IntelligenceAndCvCompetencies_AreWired()
    {
        var root = FindRoot();
        var profile = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "Profile.cshtml"));
        var profileModel = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "Profile.cshtml.cs"));
        var recordType = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Domain", "Enums",
            "EmployeeRecordType.cs"));
        var promotion = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Hrms",
            "PeopleAiStructuredRecordStore.cs"));
        var calculator = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Application", "PeopleAi",
            "EmployeeProfileIntelligence.cs"));

        Assert.Contains("Profile Completeness", profile);
        Assert.Contains("Smart Employee Summary", profile);
        Assert.Contains("EmployeeRecordType.Skill", profile);
        Assert.Contains("EmployeeRecordType.Language", profile);
        Assert.Contains("BuildProfileIntelligence", profileModel);
        Assert.Contains("Skill = 10", recordType);
        Assert.Contains("Language = 11", recordType);
        Assert.Contains("WHEN N'Skills' THEN 10", promotion);
        Assert.Contains("WHEN N'Languages' THEN 11", promotion);
        Assert.Contains("EmployeeProfileIntelligenceResult", calculator);
        Assert.Contains("MissingItems", calculator);
    }

    [Fact]
    public void PeopleAiCss_UsesDesignTokensOnly()
    {
        var root = FindRoot();
        var settingsCss = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "wwwroot", "css", "pages",
            "people-ai-settings.css"));
        var onboardingCss = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "wwwroot", "css", "pages",
            "smart-onboarding.css"));

        foreach (var css in new[] { settingsCss, onboardingCss })
        {
            Assert.Contains("var(--color-", css);
            Assert.DoesNotContain("#", css);
            Assert.DoesNotContain("rgba(", css, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PeopleAiSchema_ContainsStagingIdentityAndAuditLayers()
    {
        var root = FindRoot();
        var schema = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Hrms",
            "PeopleAiSchema.cs"));

        Assert.Contains("EmployeeOnboardingSessions", schema);
        Assert.Contains("DocumentExtractionRuns", schema);
        Assert.Contains("DocumentExtractedFields", schema);
        Assert.Contains("OnboardingValidationIssues", schema);
        Assert.Contains("PeopleAiJobs", schema);
        Assert.Contains("PeopleAiAuditLogs", schema);
        Assert.Contains("EmployeeIdentityDocuments", schema);
        Assert.Contains("CompanyPeopleAiDocumentTypes", schema);
        Assert.Contains("CompanyEmployeeDocumentPolicies", schema);
        Assert.Contains("CompanyPeopleAiFieldPolicies", schema);
        Assert.Contains("FamilyNumber", schema);
        Assert.DoesNotContain(
            "UNIQUE INDEX UX_EmployeeIdentityDocuments",
            schema,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalOcrWorker_ForcesUtf8AndPreprocessesLargeImages()
    {
        var root = FindRoot();
        var client = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "PeopleAi",
            "LocalOcrProcessClient.cs"));
        var worker = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "PeopleAI",
            "local_ocr_worker.py"));

        Assert.Contains("encoderShouldEmitUTF8Identifier: false", client);
        Assert.Contains("StandardInputEncoding = new UTF8Encoding", client);
        Assert.Contains("StandardOutputEncoding = new UTF8Encoding", client);
        Assert.Contains("PYTHONIOENCODING", client);
        Assert.Contains("PEOPLE_AI_OCR_MAX_IMAGE_SIDE", client);
        Assert.Contains("TimeoutException", client);

        Assert.Contains("prepared_ocr_input", worker);
        Assert.Contains("ImageOps.exif_transpose", worker);
        Assert.Contains("Image.Resampling.LANCZOS", worker);
        Assert.Contains("ensure_ascii=True", worker);
        Assert.Contains("sys.stdout.reconfigure", worker);
        Assert.Contains("lstrip(\"\\ufeff\")", worker);
        Assert.Contains("_recover_family_number_line", worker);
        Assert.Contains("Recovered FamilyNumber candidate", worker);
        Assert.Contains("[A-Z0-9]{13,24}", worker);
        Assert.Contains("Family numbers may be alphanumeric", worker);
    }

    [Fact]
    public void SmartOnboarding_ReportsPreciseUploadRejectionReasons()
    {
        var root = FindRoot();
        var model = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "SmartOnboarding.cshtml.cs"));
        var protectedAssets = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Security",
            "OnboardingProtectedAssetService.cs"));

        Assert.Contains("UploadRejectionMessage", model);
        Assert.Contains("FileTooLarge", model);
        Assert.Contains("SignatureMismatch", model);
        Assert.Contains("MalwareScanUnavailable", model);

        Assert.Contains("ProtectedOnboardingAssetErrorCodes", protectedAssets);
        Assert.Contains("FILE_TOO_LARGE", protectedAssets);
        Assert.Contains("SIGNATURE_MISMATCH", protectedAssets);
        Assert.Contains("MALWARE_SCAN_UNAVAILABLE", protectedAssets);
    }

    [Fact]
    public void SmartOnboarding_ExposesLiveQueueTelemetry()
    {
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees",
            "SmartOnboarding.cshtml"));
        var store = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "Hrms",
            "EmployeeOnboardingStore.cs"));
        var script = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "wwwroot", "js",
            "smart-onboarding-live-queue.js"));

        Assert.Contains("data-live-queue", page);
        Assert.Contains("data-live-fragment=\"session\"", page);
        Assert.Contains("data-live-fragment=\"next\"", page);
        Assert.Contains("data-elapsed-from", page);
        Assert.Contains("data-countdown-to", page);
        Assert.Contains("الانتهاء المتوقع", page);
        Assert.Contains("حد محافظ للمحاولة الأولى", page);
        Assert.Contains("JobsAhead", store);
        Assert.Contains("ActiveQueueCount", store);
        Assert.Contains("AverageSuccessfulDurationMs", store);
        Assert.Contains("fetch(url", script);
        Assert.Contains("syncLiveFragments(parsed)", script);
        Assert.Contains("current.replaceWith(fresh)", script);
    }

    [Fact]
    public void PeopleAiWorker_RecoversStaleProcessingJobs()
    {
        var root = FindRoot();
        var processor = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Infrastructure", "PeopleAi",
            "PeopleAiJobProcessorService.cs"));

        Assert.Contains("RecoverStaleJobsAsync", processor);
        Assert.Contains("_options.JobTimeoutSeconds + 30", processor);
        Assert.Contains("recoveryInterval", processor);
        Assert.Contains("nextRecoveryAt = utcNow.Add(recoveryInterval)", processor);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
