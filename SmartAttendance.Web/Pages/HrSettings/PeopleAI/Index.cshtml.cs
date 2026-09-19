using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.PeopleAi;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.HrSettings.PeopleAI;

[Authorize(Roles = RoleRouteCatalog.Admin)]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public IndexModel(
        ApplicationDbContext db,
        ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

    public sealed record CompanyOption(int Id, string Name);

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }
    public List<CompanyOption> Companies { get; private set; } = [];
    public CompanyPeopleAiPolicy? Policy { get; private set; }
    public List<PeopleAiDocumentTypeDefinition> DocumentTypes { get; private set; } = [];
    public List<EmployeeDocumentPolicy> DocumentPolicies { get; private set; } = [];
    public List<PeopleAiFieldPolicy> FieldPolicies { get; private set; } = [];

    public string? StatusMessage =>
        TempData["PeopleAiSettingsStatus"]?.ToString();

    public string? ErrorMessage =>
        TempData["PeopleAiSettingsError"]?.ToString();

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSavePolicyAsync(
        int companyId,
        string duplicateScope,
        string duplicateAction,
        string reviewerMode,
        bool enabled,
        string[]? languages)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (companyId <= 0 || !scope.Allows(companyId))
        {
            return Forbid();
        }

        if (!Enum.TryParse<PeopleAiDuplicateScope>(
                duplicateScope, true, out var parsedScope))
        {
            parsedScope = PeopleAiDuplicateScope.AuthorizedCompanies;
        }
        if (!Enum.TryParse<PeopleAiDuplicateAction>(
                duplicateAction, true, out var parsedAction))
        {
            parsedAction = PeopleAiDuplicateAction.RequireReview;
        }

        if (!Enum.TryParse<PeopleAiReviewerMode>(
                reviewerMode, true, out var parsedReviewer))
        {
            parsedReviewer =
                PeopleAiReviewerMode.AdminOrCreatorWithPermission;
        }

        var enabledLanguages = (languages ?? ["ar", "en"])
            .Where(x => x is "ar" or "en")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (enabledLanguages.Length == 0)
        {
            enabledLanguages = ["ar", "en"];
        }

        var policy = new CompanyPeopleAiPolicy(
            companyId,
            parsedScope,
            parsedAction,
            parsedReviewer,
            enabledLanguages,
            CloudProcessingAllowed: false,
            IsEnabled: enabled);
        await PeopleAiSettingsStore.SaveAsync(
            _db,
            policy,
            User.Identity?.Name ?? "Admin");

        TempData["PeopleAiSettingsStatus"] =
            "تم حفظ إعدادات People AI للشركة.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    public async Task<IActionResult> OnPostSaveDocumentTypeAsync(
        int companyId,
        string documentType,
        string displayLabel,
        int sortOrder,
        bool active)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (companyId <= 0 || !scope.Allows(companyId))
        {
            return Forbid();
        }

        documentType = (documentType ?? string.Empty).Trim();
        displayLabel = (displayLabel ?? string.Empty).Trim();

        if (documentType.Length == 0 || displayLabel.Length == 0)
        {
            TempData["PeopleAiSettingsError"] =
                "مفتاح نوع المستند واسم العرض مطلوبان.";
            return RedirectToPage(new { CompanyId = companyId });
        }

        await PeopleAiSettingsStore.SaveDocumentTypeAsync(
            _db,
            new PeopleAiDocumentTypeDefinition(
                companyId,
                documentType,
                displayLabel,
                sortOrder,
                active));

        TempData["PeopleAiSettingsStatus"] =
            $"تم حفظ نوع المستند {displayLabel}.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    public async Task<IActionResult> OnPostDeleteDocumentTypeAsync(
        int companyId,
        string documentType)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (companyId <= 0 || !scope.Allows(companyId))
        {
            return Forbid();
        }

        await PeopleAiSettingsStore.DisableDocumentTypeAsync(
            _db,
            companyId,
            documentType);

        TempData["PeopleAiSettingsStatus"] =
            $"تم إخفاء نوع المستند {documentType} من قائمة الرفع.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    public async Task<IActionResult> OnPostSaveDocumentPolicyAsync(
        int companyId,
        string documentType,
        string employeeCategory,
        string requirement,
        bool requireExpiryDate,
        bool requireOriginalVerification,
        bool active)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (companyId <= 0 || !scope.Allows(companyId))
        {
            return Forbid();
        }

        documentType = (documentType ?? string.Empty).Trim();
        employeeCategory = (employeeCategory ?? string.Empty).Trim();

        if (documentType.Length == 0 ||
            employeeCategory is not ("All" or "Citizen" or "Expat"))
        {
            TempData["PeopleAiSettingsError"] =
                "نوع المستند أو فئة الموظف غير صالحة.";
            return RedirectToPage(new { CompanyId = companyId });
        }
        await PeopleAiSettingsStore.SaveDocumentPolicyAsync(
            _db,
            new EmployeeDocumentPolicy(
                companyId,
                documentType,
                employeeCategory,
                requirement,
                requireExpiryDate,
                requireOriginalVerification,
                active));

        TempData["PeopleAiSettingsStatus"] =
            $"تم حفظ سياسة المستند {documentType}.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    public async Task<IActionResult> OnPostSaveFieldPolicyAsync(
        int companyId,
        string documentType,
        string fieldKey,
        string displayLabel,
        string requirement,
        int sortOrder,
        bool allowBulkApprove,
        bool active)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (companyId <= 0 || !scope.Allows(companyId))
        {
            return Forbid();
        }

        documentType = (documentType ?? string.Empty).Trim();
        fieldKey = (fieldKey ?? string.Empty).Trim();
        displayLabel = (displayLabel ?? string.Empty).Trim();

        if (documentType.Length == 0 ||
            fieldKey.Length == 0 ||
            displayLabel.Length == 0)
        {
            TempData["PeopleAiSettingsError"] =
                "نوع المستند ومفتاح الحقل واسم العرض مطلوبة.";
            return RedirectToPage(new { CompanyId = companyId });
        }

        await PeopleAiSettingsStore.SaveFieldPolicyAsync(
            _db,
            new PeopleAiFieldPolicy(
                companyId,
                documentType,
                fieldKey,
                displayLabel,
                requirement,
                sortOrder,
                allowBulkApprove,
                active));

        TempData["PeopleAiSettingsStatus"] =
            $"تم حفظ الحقل {displayLabel}.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    public async Task<IActionResult> OnPostDeleteFieldPolicyAsync(
        int companyId,
        string documentType,
        string fieldKey)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (companyId <= 0 || !scope.Allows(companyId))
        {
            return Forbid();
        }

        await PeopleAiSettingsStore.DisableFieldPolicyAsync(
            _db,
            companyId,
            documentType,
            fieldKey);

        TempData["PeopleAiSettingsStatus"] =
            $"تم إخفاء الحقل {fieldKey} من المراجعة.";
        return RedirectToPage(new { CompanyId = companyId });
    }

    private async Task LoadAsync()
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);

        Companies = (await _db.Companies
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new CompanyOption(x.Id, x.Name))
                .ToListAsync(HttpContext.RequestAborted))
            .Where(x => scope.Allows(x.Id))
            .ToList();

        CompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            CompanyId,
            Companies.Select(x => x.Id).ToArray());

        if (CompanyId is not > 0 || !scope.Allows(CompanyId.Value))
        {
            return;
        }
        Policy = await PeopleAiSettingsStore.GetAsync(
            _db,
            CompanyId.Value);

        DocumentTypes =
            await PeopleAiSettingsStore.ListDocumentTypesAsync(
                _db,
                CompanyId.Value,
                includeInactive: true);

        DocumentPolicies =
            await PeopleAiSettingsStore.ListDocumentPoliciesAsync(
                _db,
                CompanyId.Value);

        FieldPolicies =
            await PeopleAiSettingsStore.ListFieldPoliciesAsync(
                _db,
                CompanyId.Value);
    }
}
