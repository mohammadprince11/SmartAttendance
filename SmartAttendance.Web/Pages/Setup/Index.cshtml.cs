using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Branches.Services;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Setup.Services;
using SmartAttendance.Application.Setup.ViewModels;
using SmartAttendance.Domain.Enums;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.CompanyContext;

namespace SmartAttendance.Web.Pages.Setup;

public class IndexModel : PageModel
{
    private const long MaximumLogoSize = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedLogoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp"
    };

    private readonly ISetupService _setupService;
    private readonly ICompanyService _companyService;
    private readonly IBranchService _branchService;
    private readonly IDepartmentService _departmentService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public IndexModel(
        ISetupService setupService,
        ICompanyService companyService,
        IBranchService branchService,
        IDepartmentService departmentService,
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment)
    {
        _setupService = setupService;
        _companyService = companyService;
        _branchService = branchService;
        _departmentService = departmentService;
        _dbContext = dbContext;
        _environment = environment;
    }

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    [BindProperty]
    public CompanySetupProfileViewModel Profile { get; set; } = new();

    [BindProperty]
    public PayrollCutoffPolicyViewModel CutoffPolicy { get; set; } = new();

    [BindProperty]
    public BranchCreateViewModel NewBranch { get; set; } = new();

    [BindProperty]
    public DepartmentCreateViewModel NewDepartment { get; set; } = new();

    [BindProperty]
    public BranchEditViewModel EditBranch { get; set; } = new();

    [BindProperty]
    public DepartmentEditViewModel EditDepartment { get; set; } = new();

    [BindProperty]
    public IFormFile? LogoFile { get; set; }

    [BindProperty]
    public bool RemoveLogo { get; set; }

    public IReadOnlyList<CompanyListViewModel> Companies { get; private set; } = Array.Empty<CompanyListViewModel>();

    public IReadOnlyList<BranchListViewModel> Branches { get; private set; } = Array.Empty<BranchListViewModel>();

    public IReadOnlyList<DepartmentListViewModel> Departments { get; private set; } = Array.Empty<DepartmentListViewModel>();

    public int ActiveEmployeeCount { get; private set; }

    public int ActiveBranchCount { get; private set; }

    public int ActiveDepartmentCount { get; private set; }

    public IReadOnlyDictionary<int, int> ActiveEmployeeCountByBranch { get; private set; } =
        new Dictionary<int, int>();

    public IReadOnlyDictionary<int, int> ActiveEmployeeCountByDepartment { get; private set; } =
        new Dictionary<int, int>();

    public bool IsCompanyDeactivationLocked =>
        (Setup?.Profile.IsActive ?? Profile.IsActive) &&
        (ActiveEmployeeCount > 0 ||
         ActiveBranchCount > 0 ||
         ActiveDepartmentCount > 0);

    public CompanySetupViewModel? Setup { get; private set; }

    public IReadOnlyList<SelectListItem> CountryOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> CurrencyOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> TimeZoneOptions { get; private set; } = Array.Empty<SelectListItem>();

    public IReadOnlyList<SelectListItem> CutoffTypeOptions { get; private set; } = Array.Empty<SelectListItem>();

    public bool OpenPolicyModal { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? editPolicyId, bool newPolicy = false)
    {
        Companies = await LoadCompaniesAsync();
        BuildOptions();

        if (Companies.Count == 0)
        {
            return Page();
        }

        var selectedCompanyId = CompanySelectionContext.Resolve(
            HttpContext,
            CompanyId,
            Companies.Select(x => x.Id).ToArray());

        if (!selectedCompanyId.HasValue)
        {
            return Page();
        }

        if (!await LoadSetupAsync(selectedCompanyId.Value))
        {
            return NotFound();
        }

        Profile = Setup!.Profile;

        if (newPolicy)
        {
            if (!HasAvailableCutoffTypes())
            {
                TempData["ErrorMessage"] =
                    "\u062c\u0645\u064a\u0639 \u0623\u0646\u0648\u0627\u0639 \u0627\u0644\u0628\u064a\u0627\u0646\u0627\u062a \u0645\u0631\u062a\u0628\u0637\u0629 \u0628\u0633\u064a\u0627\u0633\u0627\u062a \u063a\u0644\u0642 \u0641\u0639\u0627\u0644\u0629. " +
                    "\u0639\u0637\u0644 \u0625\u062d\u062f\u0649 \u0627\u0644\u0633\u064a\u0627\u0633\u0627\u062a \u0623\u0648 \u062d\u0631\u0631 \u0646\u0648\u0639\u0627 \u0642\u0628\u0644 \u0625\u0636\u0627\u0641\u0629 \u0633\u064a\u0627\u0633\u0629 \u062c\u062f\u064a\u062f\u0629.";

                return RedirectToPage("./Index", new { companyId = selectedCompanyId.Value });
            }

            CutoffPolicy = CreateNewPolicy(selectedCompanyId.Value);
            OpenPolicyModal = true;
        }
        else if (editPolicyId.HasValue)
        {
            var policy = await _setupService.GetPayrollCutoffPolicyAsync(selectedCompanyId.Value, editPolicyId.Value);

            if (policy != null)
            {
                CutoffPolicy = policy;
                OpenPolicyModal = true;
            }
            else
            {
                TempData["ErrorMessage"] = "\u0644\u0645 \u064a\u062a\u0645 \u0627\u0644\u0639\u062b\u0648\u0631 \u0639\u0644\u0649 \u0633\u064a\u0627\u0633\u0629 \u0627\u0644\u063a\u0644\u0642.";
                CutoffPolicy = CreateNewPolicy(selectedCompanyId.Value);
            }
        }
        else
        {
            CutoffPolicy = CreateNewPolicy(selectedCompanyId.Value);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCreateBranchAsync()
    {
        var companyId = NewBranch.CompanyId;

        if (!await LoadSetupAsync(companyId))
        {
            return NotFound();
        }

        NewBranch.CompanyId = companyId;
        NewBranch.Code = string.Empty;
        NewBranch.Name = NewBranch.Name?.Trim() ?? string.Empty;
        NewBranch.Address = string.IsNullOrWhiteSpace(NewBranch.Address)
            ? null
            : NewBranch.Address.Trim();

        if (string.IsNullOrWhiteSpace(NewBranch.Name))
        {
            TempData["ErrorMessage"] =
                "\u064a\u062c\u0628 \u0625\u062f\u062e\u0627\u0644 \u0627\u0633\u0645 \u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644.";

            return RedirectToSetupSection(companyId, "work-locations");
        }

        var created = await _branchService.CreateAsync(NewBranch);

        TempData[created ? "SuccessMessage" : "ErrorMessage"] = created
            ? "\u062a\u0645\u062a \u0625\u0636\u0627\u0641\u0629 \u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644 \u0628\u0646\u062c\u0627\u062d."
            : "\u062a\u0639\u0630\u0631\u062a \u0625\u0636\u0627\u0641\u0629 \u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644. \u062a\u0623\u0643\u062f \u0645\u0646 \u0628\u064a\u0627\u0646\u0627\u062a \u0627\u0644\u0634\u0631\u0643\u0629 \u0648\u0639\u062f\u0645 \u062a\u0643\u0631\u0627\u0631 \u0627\u0644\u0627\u0633\u0645.";

        return RedirectToSetupSection(companyId, "work-locations");
    }

    public async Task<IActionResult> OnPostCreateDepartmentAsync()
    {
        var companyId = NewDepartment.CompanyId;

        if (!await LoadSetupAsync(companyId))
        {
            return NotFound();
        }

        NewDepartment.Code = string.Empty;
        NewDepartment.Name = NewDepartment.Name?.Trim() ?? string.Empty;
        NewDepartment.CompanyId = companyId;
        NewDepartment.BranchId = 0;

        if (string.IsNullOrWhiteSpace(NewDepartment.Name))
        {
            TempData["ErrorMessage"] =
                "\u064a\u062c\u0628 \u0625\u062f\u062e\u0627\u0644 \u0627\u0633\u0645 \u0627\u0644\u0642\u0633\u0645.";

            return RedirectToSetupSection(companyId, "departments");
        }

        var created = await _departmentService.CreateAsync(NewDepartment);

        TempData[created ? "SuccessMessage" : "ErrorMessage"] = created
            ? "\u062a\u0645\u062a \u0625\u0636\u0627\u0641\u0629 \u0627\u0644\u0642\u0633\u0645 \u0628\u0646\u062c\u0627\u062d."
            : "\u062a\u0639\u0630\u0631\u062a \u0625\u0636\u0627\u0641\u0629 \u0627\u0644\u0642\u0633\u0645. \u062a\u0623\u0643\u062f \u0645\u0646 \u0627\u0644\u0634\u0631\u0643\u0629 \u0648\u0639\u062f\u0645 \u062a\u0643\u0631\u0627\u0631 \u0627\u0644\u0627\u0633\u0645.";

        return RedirectToSetupSection(companyId, "departments");
    }

    public async Task<IActionResult> OnPostUpdateBranchAsync()
    {
        var requestedCompanyId = EditBranch.CompanyId;
        var existing = await _branchService.GetEditByIdAsync(EditBranch.Id);

        if (existing == null ||
            existing.CompanyId != requestedCompanyId)
        {
            TempData["ErrorMessage"] =
                "\u062a\u0639\u0630\u0631 \u0627\u0644\u0639\u062b\u0648\u0631 \u0639\u0644\u0649 \u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644 \u0627\u0644\u0645\u062d\u062f\u062f.";

            return RedirectToSetupSection(
                requestedCompanyId,
                "work-locations");
        }

        EditBranch.Name = EditBranch.Name?.Trim() ?? string.Empty;
        EditBranch.Address = string.IsNullOrWhiteSpace(EditBranch.Address)
            ? null
            : EditBranch.Address.Trim();
        EditBranch.CompanyId = existing.CompanyId;
        EditBranch.Code = existing.Code;

        if (string.IsNullOrWhiteSpace(EditBranch.Name))
        {
            TempData["ErrorMessage"] =
                "\u064a\u062c\u0628 \u0625\u062f\u062e\u0627\u0644 \u0627\u0633\u0645 \u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644.";

            return RedirectToSetupSection(
                existing.CompanyId,
                "work-locations");
        }

        var updated = await _branchService.UpdateAsync(EditBranch);

        TempData[updated ? "SuccessMessage" : "ErrorMessage"] = updated
            ? "\u062a\u0645 \u062a\u062d\u062f\u064a\u062b \u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644 \u0628\u0646\u062c\u0627\u062d."
            : "\u062a\u0639\u0630\u0631 \u062a\u062d\u062f\u064a\u062b \u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644. \u062a\u0623\u0643\u062f \u0645\u0646 \u0639\u062f\u0645 \u062a\u0643\u0631\u0627\u0631 \u0627\u0644\u0627\u0633\u0645\u060c \u0648\u0623\u0646 \u0627\u0644\u0634\u0631\u0643\u0629 \u0641\u0639\u0627\u0644\u0629\u060c \u0648\u0639\u062f\u0645 \u0648\u062c\u0648\u062f \u0645\u0648\u0638\u0641\u064a\u0646 \u0641\u0639\u0627\u0644\u064a\u0646 \u0639\u0646\u062f \u0645\u062d\u0627\u0648\u0644\u0629 \u0627\u0644\u062a\u0639\u0637\u064a\u0644.";

        return RedirectToSetupSection(
            existing.CompanyId,
            "work-locations");
    }

    public async Task<IActionResult> OnPostDeleteBranchAsync(
        int companyId,
        int branchId)
    {
        var branch = await _branchService.GetByIdAsync(branchId);

        if (branch == null ||
            branch.CompanyId != companyId)
        {
            TempData["ErrorMessage"] =
                "\u062a\u0639\u0630\u0631 \u0627\u0644\u0639\u062b\u0648\u0631 \u0639\u0644\u0649 \u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644 \u0627\u0644\u0645\u062d\u062f\u062f.";

            return RedirectToSetupSection(
                companyId,
                "work-locations");
        }

        var deleted = await _branchService.DeleteAsync(branchId);

        TempData[deleted ? "SuccessMessage" : "ErrorMessage"] = deleted
            ? "\u062a\u0645 \u062d\u0630\u0641 \u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644 \u0628\u0646\u062c\u0627\u062d."
            : "\u062a\u0639\u0630\u0631 \u062d\u0630\u0641 \u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644 \u0644\u0648\u062c\u0648\u062f \u0628\u064a\u0627\u0646\u0627\u062a \u0645\u0631\u062a\u0628\u0637\u0629 \u0628\u0647.";

        return RedirectToSetupSection(
            companyId,
            "work-locations");
    }

    public async Task<IActionResult> OnPostUpdateDepartmentAsync()
    {
        var requestedCompanyId = EditDepartment.CompanyId;
        var existing = await _departmentService.GetEditByIdAsync(
            EditDepartment.Id);

        if (existing == null ||
            existing.CompanyId != requestedCompanyId)
        {
            TempData["ErrorMessage"] =
                "\u062a\u0639\u0630\u0631 \u0627\u0644\u0639\u062b\u0648\u0631 \u0639\u0644\u0649 \u0627\u0644\u0642\u0633\u0645 \u0627\u0644\u0645\u062d\u062f\u062f.";

            return RedirectToSetupSection(
                requestedCompanyId,
                "departments");
        }

        EditDepartment.Name =
            EditDepartment.Name?.Trim() ?? string.Empty;
        EditDepartment.CompanyId = existing.CompanyId;
        EditDepartment.Code = existing.Code;
        EditDepartment.BranchId = 0;

        if (string.IsNullOrWhiteSpace(EditDepartment.Name))
        {
            TempData["ErrorMessage"] =
                "\u064a\u062c\u0628 \u0625\u062f\u062e\u0627\u0644 \u0627\u0633\u0645 \u0627\u0644\u0642\u0633\u0645.";

            return RedirectToSetupSection(
                existing.CompanyId,
                "departments");
        }

        var updated = await _departmentService.UpdateAsync(
            EditDepartment);

        TempData[updated ? "SuccessMessage" : "ErrorMessage"] = updated
            ? "\u062a\u0645 \u062a\u062d\u062f\u064a\u062b \u0627\u0644\u0642\u0633\u0645 \u0628\u0646\u062c\u0627\u062d."
            : "\u062a\u0639\u0630\u0631 \u062a\u062d\u062f\u064a\u062b \u0627\u0644\u0642\u0633\u0645. \u062a\u0623\u0643\u062f \u0645\u0646 \u0639\u062f\u0645 \u062a\u0643\u0631\u0627\u0631 \u0627\u0644\u0627\u0633\u0645\u060c \u0648\u0623\u0646 \u0627\u0644\u0634\u0631\u0643\u0629 \u0641\u0639\u0627\u0644\u0629\u060c \u0648\u0639\u062f\u0645 \u0648\u062c\u0648\u062f \u0645\u0648\u0638\u0641\u064a\u0646 \u0641\u0639\u0627\u0644\u064a\u0646 \u0639\u0646\u062f \u0645\u062d\u0627\u0648\u0644\u0629 \u0627\u0644\u062a\u0639\u0637\u064a\u0644.";

        return RedirectToSetupSection(
            existing.CompanyId,
            "departments");
    }

    public async Task<IActionResult> OnPostDeleteDepartmentAsync(
        int companyId,
        int departmentId)
    {
        var department = await _departmentService.GetByIdAsync(
            departmentId);

        if (department == null ||
            department.CompanyId != companyId)
        {
            TempData["ErrorMessage"] =
                "\u062a\u0639\u0630\u0631 \u0627\u0644\u0639\u062b\u0648\u0631 \u0639\u0644\u0649 \u0627\u0644\u0642\u0633\u0645 \u0627\u0644\u0645\u062d\u062f\u062f.";

            return RedirectToSetupSection(
                companyId,
                "departments");
        }

        var deleted = await _departmentService.DeleteAsync(
            departmentId);

        TempData[deleted ? "SuccessMessage" : "ErrorMessage"] = deleted
            ? "\u062a\u0645 \u062d\u0630\u0641 \u0627\u0644\u0642\u0633\u0645 \u0628\u0646\u062c\u0627\u062d."
            : "\u062a\u0639\u0630\u0631 \u062d\u0630\u0641 \u0627\u0644\u0642\u0633\u0645 \u0644\u0648\u062c\u0648\u062f \u0628\u064a\u0627\u0646\u0627\u062a \u0645\u0631\u062a\u0628\u0637\u0629 \u0628\u0647.";

        return RedirectToSetupSection(
            companyId,
            "departments");
    }

    public async Task<IActionResult> OnPostSaveProfileAsync()
    {
        CompanyId = Profile.CompanyId;

        if (!await LoadSetupAsync(Profile.CompanyId))
        {
            return NotFound();
        }

        Profile.CompanyCode = Setup!.CompanyCode;
        var existingLogoPath = Setup.Profile.LogoPath;
        Profile.LogoPath = existingLogoPath;

        ModelState.Clear();
        TryValidateModel(Profile, nameof(Profile));

        var logoValidationError = ValidateLogo(LogoFile);
        if (logoValidationError != null)
        {
            ModelState.AddModelError(nameof(LogoFile), logoValidationError);
        }

        if (!ModelState.IsValid)
        {
            CutoffPolicy = CreateNewPolicy(Profile.CompanyId);
            return Page();
        }

        string? newLogoPath = null;

        if (LogoFile != null && LogoFile.Length > 0)
        {
            newLogoPath = await SaveLogoAsync(Profile.CompanyId, LogoFile);
            Profile.LogoPath = newLogoPath;
        }
        else if (RemoveLogo)
        {
            Profile.LogoPath = null;
        }

        try
        {
            var result = await _setupService.UpdateCompanyProfileAsync(Profile);

            if (!result.Success)
            {
                if (newLogoPath != null)
                {
                    DeleteStoredLogo(newLogoPath);
                }

                TempData["ErrorMessage"] = result.Message;

                return RedirectToSetupSection(
                    Profile.CompanyId,
                    "company-profile");
            }
        }
        catch
        {
            if (newLogoPath != null)
            {
                DeleteStoredLogo(newLogoPath);
            }

            throw;
        }

        if (!string.Equals(existingLogoPath, Profile.LogoPath, StringComparison.OrdinalIgnoreCase))
        {
            DeleteStoredLogo(existingLogoPath);
        }

        TempData["SuccessMessage"] = "\u062a\u0645 \u062d\u0641\u0638 \u0628\u064a\u0627\u0646\u0627\u062a \u0627\u0644\u0634\u0631\u0643\u0629 \u0628\u0646\u062c\u0627\u062d.";
        return RedirectToPage("./Index", new { companyId = Profile.CompanyId });
    }

    public async Task<IActionResult> OnPostSaveCutoffAsync()
    {
        CompanyId = CutoffPolicy.CompanyId;

        if (!await LoadSetupAsync(CutoffPolicy.CompanyId))
        {
            return NotFound();
        }

        CutoffPolicy.PolicyTypes = CutoffPolicy.PolicyTypes
            .Where(Enum.IsDefined)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        ModelState.Clear();

        if (CutoffPolicy.PolicyTypes.Count == 0)
        {
            ModelState.AddModelError(
                "CutoffPolicy.PolicyTypes",
                "\u064a\u062c\u0628 \u0627\u062e\u062a\u064a\u0627\u0631 \u0646\u0648\u0639 \u0628\u064a\u0627\u0646\u0627\u062a \u0648\u0627\u062d\u062f \u0639\u0644\u0649 \u0627\u0644\u0623\u0642\u0644.");
        }

        TryValidateModel(CutoffPolicy, nameof(CutoffPolicy));

        if (!ModelState.IsValid)
        {
            Profile = Setup!.Profile;
            OpenPolicyModal = true;
            return Page();
        }

        var result = await _setupService.SavePayrollCutoffPolicyAsync(
            CutoffPolicy,
            CutoffPolicy.PolicyTypes);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
            Profile = Setup!.Profile;
            OpenPolicyModal = true;
            return Page();
        }

        TempData["SuccessMessage"] = CutoffPolicy.Id > 0
            ? "\u062a\u0645 \u062a\u0639\u062f\u064a\u0644 \u0633\u064a\u0627\u0633\u0629 \u0627\u0644\u063a\u0644\u0642 \u0628\u0646\u062c\u0627\u062d."
            : "\u062a\u0645 \u0625\u0636\u0627\u0641\u0629 \u0633\u064a\u0627\u0633\u0629 \u0627\u0644\u063a\u0644\u0642 \u0628\u0646\u062c\u0627\u062d.";

        return RedirectToPage("./Index", new { companyId = CutoffPolicy.CompanyId });
    }

    public async Task<IActionResult> OnPostDeleteCutoffAsync(int companyId, int policyId)
    {
        var result = await _setupService.DeletePayrollCutoffPolicyAsync(companyId, policyId);

        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] = result.Success
            ? "\u062a\u0645 \u062d\u0630\u0641 \u0633\u064a\u0627\u0633\u0629 \u0627\u0644\u063a\u0644\u0642 \u0628\u0646\u062c\u0627\u062d."
            : result.Message;

        return RedirectToPage("./Index", new { companyId });
    }

    public string GetCutoffTypeLabel(PayrollCutoffType value)
    {
        return value switch
        {
            PayrollCutoffType.Attendance => "\u0627\u0644\u062d\u0636\u0648\u0631 \u0648\u0627\u0644\u0627\u0646\u0635\u0631\u0627\u0641",
            PayrollCutoffType.WorkingDays => "\u0623\u064a\u0627\u0645 \u0627\u0644\u0639\u0645\u0644",
            PayrollCutoffType.Overtime => "\u0627\u0644\u0639\u0645\u0644 \u0627\u0644\u0625\u0636\u0627\u0641\u064a",
            PayrollCutoffType.Deductions => "\u0627\u0644\u0627\u0633\u062a\u0642\u0637\u0627\u0639\u0627\u062a",
            PayrollCutoffType.Additions => "\u0627\u0644\u0625\u0636\u0627\u0641\u0627\u062a",
            PayrollCutoffType.Hiring => "\u0627\u0644\u062a\u0639\u064a\u064a\u0646\u0627\u062a",
            PayrollCutoffType.SalaryChanges => "\u062a\u063a\u064a\u064a\u0631\u0627\u062a \u0627\u0644\u0631\u0627\u062a\u0628",
            PayrollCutoffType.Leaves => "\u0627\u0644\u0625\u062c\u0627\u0632\u0627\u062a",
            PayrollCutoffType.Penalties => "\u0627\u0644\u0639\u0642\u0648\u0628\u0627\u062a",
            PayrollCutoffType.Terminations => "\u0625\u0646\u0647\u0627\u0621 \u0627\u0644\u062e\u062f\u0645\u0629",
            _ => value.ToString()
        };
    }

    // NEXORA_ACTIVE_CUTOFF_TYPE_UI_START
    public string? GetActiveCutoffTypeOwner(
        PayrollCutoffType policyType,
        int currentPolicyId = 0)
    {
        return Setup?.CutoffPolicies
            .Where(x =>
                x.IsActive &&
                x.Id != currentPolicyId &&
                x.PolicyTypes.Contains(policyType))
            .OrderBy(x => x.Name)
            .Select(x => x.Name)
            .FirstOrDefault();
    }

    public int GetActiveEmployeeCountForBranch(int branchId)
    {
        return ActiveEmployeeCountByBranch.TryGetValue(branchId, out var count)
            ? count
            : 0;
    }

    public int GetActiveEmployeeCountForDepartment(int departmentId)
    {
        return ActiveEmployeeCountByDepartment.TryGetValue(departmentId, out var count)
            ? count
            : 0;
    }

    public bool HasAvailableCutoffTypes(int currentPolicyId = 0)
    {
        if (Setup == null)
        {
            return false;
        }

        return Enum.GetValues<PayrollCutoffType>()
            .Any(x => GetActiveCutoffTypeOwner(x, currentPolicyId) == null);
    }
    // NEXORA_ACTIVE_CUTOFF_TYPE_UI_END

    private async Task<bool> LoadSetupAsync(int companyId)
    {
        Companies = await LoadCompaniesAsync();
        BuildOptions();

        if (!Companies.Any(x => x.Id == companyId))
        {
            Branches = Array.Empty<BranchListViewModel>();
            Departments = Array.Empty<DepartmentListViewModel>();
            return false;
        }

        CompanyId = companyId;
        CompanySelectionContext.Persist(HttpContext, companyId);
        Setup = await _setupService.GetCompanySetupAsync(companyId);

        if (Setup == null)
        {
            Branches = Array.Empty<BranchListViewModel>();
            Departments = Array.Empty<DepartmentListViewModel>();
            return false;
        }

        Branches = (await _branchService.GetAllAsync())
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .ToList();

        Departments = (await _departmentService.GetAllAsync())
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .ToList();

        var activeAssignments = await _dbContext.Employees
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.IsActive &&
                !x.Branch.IsDeleted &&
                x.Branch.CompanyId == companyId)
            .Select(x => new
            {
                x.BranchId,
                x.DepartmentId
            })
            .ToListAsync();

        ActiveEmployeeCount = activeAssignments.Count;
        ActiveBranchCount = Branches.Count(x => x.IsActive);
        ActiveDepartmentCount = Departments.Count(x => x.IsActive);
        ActiveEmployeeCountByBranch = activeAssignments
            .GroupBy(x => x.BranchId)
            .ToDictionary(x => x.Key, x => x.Count());
        ActiveEmployeeCountByDepartment = activeAssignments
            .GroupBy(x => x.DepartmentId)
            .ToDictionary(x => x.Key, x => x.Count());

        NewBranch.CompanyId = companyId;
        NewDepartment.CompanyId = companyId;
        NewDepartment.BranchId = 0;

        return true;
    }

    private async Task<IReadOnlyList<CompanyListViewModel>> LoadCompaniesAsync()
    {
        return (await _companyService.GetAllAsync())
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .ToList();
    }

    private IActionResult RedirectToSetupSection(int companyId, string sectionId)
    {
        return RedirectToPage(
            "./Index",
            new
            {
                companyId,
                focusSection = sectionId
            });
    }

    private static PayrollCutoffPolicyViewModel CreateNewPolicy(int companyId)
    {
        return new PayrollCutoffPolicyViewModel
        {
            CompanyId = companyId,
            FromDay = 1,
            ToDay = 30,
            IsActive = true
        };
    }

    private void BuildOptions()
    {
        CountryOptions = BuildCountryOptions();
        CurrencyOptions = BuildCurrencyOptions();
        TimeZoneOptions = TimeZoneInfo.GetSystemTimeZones()
            .OrderBy(x => x.BaseUtcOffset)
            .ThenBy(x => x.DisplayName)
            .Select(x => new SelectListItem(
                string.Format(CultureInfo.InvariantCulture, "{0} - {1}", x.Id, x.DisplayName),
                x.Id))
            .ToList();

        CutoffTypeOptions = Enum.GetValues<PayrollCutoffType>()
            .Select(x => new SelectListItem(
                GetCutoffTypeLabel(x),
                ((int)x).ToString(CultureInfo.InvariantCulture)))
            .ToList();
    }

    private static IReadOnlyList<SelectListItem> BuildCountryOptions()
    {
        var regions = new Dictionary<string, RegionInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);

                if (!regions.ContainsKey(region.TwoLetterISORegionName))
                {
                    regions[region.TwoLetterISORegionName] = region;
                }
            }
            catch
            {
            }
        }

        return regions.Values
            .OrderBy(x => x.EnglishName)
            .Select(x => new SelectListItem(
                string.Format(CultureInfo.InvariantCulture, "{0} - {1}", x.TwoLetterISORegionName, x.EnglishName),
                x.TwoLetterISORegionName))
            .ToList();
    }

    private static IReadOnlyList<SelectListItem> BuildCurrencyOptions()
    {
        var currencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            try
            {
                var region = new RegionInfo(culture.Name);

                if (!currencies.ContainsKey(region.ISOCurrencySymbol))
                {
                    currencies[region.ISOCurrencySymbol] = region.CurrencyEnglishName;
                }
            }
            catch
            {
            }
        }

        return currencies
            .OrderBy(x => x.Value)
            .ThenBy(x => x.Key)
            .Select(x => new SelectListItem(
                string.Format(CultureInfo.InvariantCulture, "{0} - {1}", x.Key, x.Value),
                x.Key))
            .ToList();
    }

    private static string? ValidateLogo(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return null;
        }

        var extension = Path.GetExtension(file.FileName);

        if (string.IsNullOrWhiteSpace(extension) || !AllowedLogoExtensions.Contains(extension))
        {
            return "\u0646\u0648\u0639 \u0627\u0644\u0634\u0639\u0627\u0631 \u063a\u064a\u0631 \u0645\u062f\u0639\u0648\u0645. \u0627\u0633\u062a\u062e\u062f\u0645 PNG \u0623\u0648 JPG \u0623\u0648 WEBP.";
        }

        if (file.Length > MaximumLogoSize)
        {
            return "\u062d\u062c\u0645 \u0627\u0644\u0634\u0639\u0627\u0631 \u064a\u062c\u0628 \u0623\u0644\u0627 \u064a\u062a\u062c\u0627\u0648\u0632 5MB.";
        }

        if (!string.IsNullOrWhiteSpace(file.ContentType) &&
            !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return "\u0627\u0644\u0645\u0644\u0641 \u0627\u0644\u0645\u0631\u0641\u0648\u0639 \u0644\u064a\u0633 \u0635\u0648\u0631\u0629 \u0635\u062d\u064a\u062d\u0629.";
        }

        return null;
    }

    private async Task<string> SaveLogoAsync(int companyId, IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var uploadsRoot = Path.Combine(_environment.WebRootPath, "uploads", "company-logos");
        Directory.CreateDirectory(uploadsRoot);

        var fileName = string.Format(
            CultureInfo.InvariantCulture,
            "company_{0}_{1}_{2}{3}",
            companyId,
            DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture),
            Guid.NewGuid().ToString("N"),
            extension);

        var fullPath = Path.Combine(uploadsRoot, fileName);

        await using var stream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(stream);

        return "/uploads/company-logos/" + fileName;
    }

    private void DeleteStoredLogo(string? relativePath)
    {
        const string allowedPrefix = "/uploads/company-logos/";

        if (string.IsNullOrWhiteSpace(relativePath) ||
            !relativePath.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var fileName = Path.GetFileName(relativePath);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        var uploadsRoot = Path.GetFullPath(Path.Combine(_environment.WebRootPath, "uploads", "company-logos"));
        var fullPath = Path.GetFullPath(Path.Combine(uploadsRoot, fileName));

        if (!fullPath.StartsWith(uploadsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (System.IO.File.Exists(fullPath))
        {
            System.IO.File.Delete(fullPath);
        }
    }
}
