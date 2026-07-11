using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

public class CreateModel : PageModel
{
    private readonly IEmployeeService _employeeService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    private static readonly HashSet<string> AllowedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx", ".xls", ".xlsx"
    };


    private static readonly HashSet<string> AllowedEmployeePhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    public CreateModel(
        IEmployeeService employeeService,
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment)
    {
        _employeeService = employeeService;
        _dbContext = dbContext;
        _environment = environment;
    }

    [BindProperty]
    public EmployeeCreateViewModel Employee { get; set; } = new();


    [BindProperty]
    public IFormFile? EmployeePhoto { get; set; }

    [BindProperty]
    public List<string> InitialDocumentTypes { get; set; } = new();

    [BindProperty]
    public List<string> InitialDocumentRequired { get; set; } = new();

    [BindProperty]
    public List<IFormFile> InitialDocumentFiles { get; set; } = new();

    public IEnumerable<BranchListViewModel> Branches { get; set; } = new List<BranchListViewModel>();

    public IEnumerable<DepartmentListViewModel> Departments { get; set; } = new List<DepartmentListViewModel>();

    public List<EmployeeProfileDynamicSection> ProfileDynamicSections { get; set; } = new();

    public IEnumerable<PositionOptionViewModel> PositionOptions { get; set; } = new List<PositionOptionViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Branches = await _employeeService.GetBranchesForDropdownAsync();
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();
        PositionOptions = await _employeeService.GetPositionsForDropdownAsync();
        ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, 0);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Branches = await _employeeService.GetBranchesForDropdownAsync();
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();
        PositionOptions = await _employeeService.GetPositionsForDropdownAsync();
        ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, 0);

        if (!ModelState.IsValid)
            return Page();

        var created = await _employeeService.CreateAsync(Employee);

        if (!created)
        {
            ErrorMessage = "\u062a\u0639\u0630\u0631 \u0625\u0646\u0634\u0627\u0621 \u0627\u0644\u0645\u0648\u0638\u0641. \u062a\u0623\u0643\u062f \u0645\u0646 \u0643\u0648\u062f \u0627\u0644\u0645\u0648\u0638\u0641 \u0648\u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644 \u0648\u0627\u0644\u0642\u0633\u0645 \u0648\u0623\u0646\u0647\u0627 \u062a\u062a\u0628\u0639 \u0625\u0644\u0649 \u0646\u0641\u0633 \u0627\u0644\u0634\u0631\u0643\u0629.";
            return Page();
        }

        var employeeId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT TOP 1 Id FROM Employees WHERE EmployeeNo = @EmployeeNo ORDER BY Id DESC",
            command => HrmsDatabase.AddParameter(command, "@EmployeeNo", Employee.EmployeeNo));

        if (employeeId > 0)
        {
            await EmployeeProfileDynamicFields.SaveAsync(_dbContext, employeeId, Request.Form);
            var photoResult = await SaveEmployeePhotoAsync(employeeId);
            var documentResult = await SaveInitialDocumentsAsync(employeeId);
            var extraResult = string.Join(" ", new[] { photoResult, documentResult }.Where(x => !string.IsNullOrWhiteSpace(x)));

            if (!string.IsNullOrWhiteSpace(extraResult))
            {
                TempData["SuccessMessage"] = $"\u062a\u0645 \u0625\u0646\u0634\u0627\u0621 \u0627\u0644\u0645\u0648\u0638\u0641 \u0628\u0646\u062c\u0627\u062d. {extraResult}";
            }
            else
            {
                TempData["SuccessMessage"] = "\u062a\u0645 \u0625\u0646\u0634\u0627\u0621 \u0627\u0644\u0645\u0648\u0638\u0641 \u0628\u0646\u062c\u0627\u062d.";
            }
        }
        else
        {
            TempData["SuccessMessage"] = "\u062a\u0645 \u0625\u0646\u0634\u0627\u0621 \u0627\u0644\u0645\u0648\u0638\u0641 \u0628\u0646\u062c\u0627\u062d.";
        }

        return RedirectToPage("./Index");
    }


    private async Task<string> SaveEmployeePhotoAsync(int employeeId)
    {
        if (EmployeePhoto == null || EmployeePhoto.Length == 0)
            return string.Empty;

        var extension = Path.GetExtension(EmployeePhoto.FileName);

        if (string.IsNullOrWhiteSpace(extension) || !AllowedEmployeePhotoExtensions.Contains(extension))
            return "\u0644\u0645 \u064a\u062a\u0645 \u062d\u0641\u0638 \u0635\u0648\u0631\u0629 \u0627\u0644\u0645\u0648\u0638\u0641 \u0644\u0623\u0646 \u0627\u0644\u0635\u064a\u063a\u0629 \u063a\u064a\u0631 \u0645\u062f\u0639\u0648\u0645\u0629.";

        if (EmployeePhoto.Length > 5 * 1024 * 1024)
            return "\u0644\u0645 \u064a\u062a\u0645 \u062d\u0641\u0638 \u0635\u0648\u0631\u0629 \u0627\u0644\u0645\u0648\u0638\u0641 \u0644\u0623\u0646 \u062d\u062c\u0645\u0647\u0627 \u0623\u0643\u0628\u0631 \u0645\u0646 5MB.";

        var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "employee-photos");
        Directory.CreateDirectory(uploadRoot);

        var storedName = $"employee_{employeeId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(uploadRoot, storedName);
        var relativePath = $"/uploads/employee-photos/{storedName}";

        await using (var stream = System.IO.File.Create(physicalPath))
        {
            await EmployeePhoto.CopyToAsync(stream);
        }

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            "UPDATE Employees SET PhotoPath = @PhotoPath WHERE Id = @EmployeeId;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@PhotoPath", relativePath);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            });

        return "\u062a\u0645 \u062d\u0641\u0638 \u0635\u0648\u0631\u0629 \u0627\u0644\u0645\u0648\u0638\u0641.";
    }

    private async Task<string> SaveInitialDocumentsAsync(int employeeId)
    {
        if (InitialDocumentFiles == null || InitialDocumentFiles.Count == 0)
            return string.Empty;

        var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "employee-documents");
        Directory.CreateDirectory(uploadRoot);

        var savedCount = 0;
        var skippedCount = 0;

        for (var i = 0; i < InitialDocumentFiles.Count; i++)
        {
            var file = InitialDocumentFiles[i];

            if (file == null || file.Length == 0)
            {
                skippedCount++;
                continue;
            }

            var extension = Path.GetExtension(file.FileName);

            if (string.IsNullOrWhiteSpace(extension) || !AllowedDocumentExtensions.Contains(extension))
            {
                skippedCount++;
                continue;
            }

            if (file.Length > 10 * 1024 * 1024)
            {
                skippedCount++;
                continue;
            }

            var documentType = InitialDocumentTypes.Count > i && !string.IsNullOrWhiteSpace(InitialDocumentTypes[i])
                ? InitialDocumentTypes[i].Trim()
                : "Other";

            var requiredText = InitialDocumentRequired.Count > i && !string.IsNullOrWhiteSpace(InitialDocumentRequired[i])
                ? InitialDocumentRequired[i].Trim()
                : "Optional";

            var safeOriginalName = Path.GetFileName(file.FileName);
            var storedName = $"{employeeId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
            var physicalPath = Path.Combine(uploadRoot, storedName);
            var relativePath = $"/uploads/employee-documents/{storedName}";

            await using (var stream = System.IO.File.Create(physicalPath))
            {
                await file.CopyToAsync(stream);
            }

            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                """
INSERT INTO EmployeeDocuments
(EmployeeId, DocumentType, FileName, StoredPath, ExpiryDate, Notes, UploadedBy)
VALUES
(@EmployeeId, @DocumentType, @FileName, @StoredPath, NULL, @Notes, @UploadedBy);

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('EmployeeDocument', CAST(@EmployeeId AS nvarchar(80)), 'Upload Document On Create', @NewValues, @UploadedBy, @IpAddress);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    HrmsDatabase.AddParameter(command, "@DocumentType", documentType);
                    HrmsDatabase.AddParameter(command, "@FileName", safeOriginalName);
                    HrmsDatabase.AddParameter(command, "@StoredPath", relativePath);
                    HrmsDatabase.AddParameter(command, "@Notes", $"Uploaded during employee creation. Required: {requiredText}.");
                    HrmsDatabase.AddParameter(command, "@UploadedBy", User?.Identity?.Name ?? "HR");
                    HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(
                        ("DocumentType", documentType),
                        ("Required", requiredText),
                        ("FileName", safeOriginalName),
                        ("StoredPath", relativePath)));
                    HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
                });

            savedCount++;
        }

        if (savedCount == 0)
            return skippedCount > 0 ? "\u0644\u0645 \u064a\u062a\u0645 \u062d\u0641\u0638 \u0623\u064a \u0645\u0633\u062a\u0645\u0633\u0643 \u0644\u0623\u0646 \u0627\u0644\u0645\u0644\u0641\u0627\u062a \u063a\u064a\u0631 \u0635\u0627\u0644\u062d\u0629 \u0623\u0648 \u0641\u0627\u0631\u063a\u0629." : string.Empty;

        return skippedCount > 0
            ? $"\u062a\u0645 \u062d\u0641\u0638 {savedCount} \u0645\u0633\u062a\u0645\u0633\u0643\u060c \u0648\u062a\u0645 \u062a\u062c\u0627\u0648\u0632 {skippedCount} \u0645\u0644\u0641 \u063a\u064a\u0631 \u0635\u0627\u0644\u062d."
            : $"\u062a\u0645 \u062d\u0641\u0638 {savedCount} \u0645\u0633\u062a\u0645\u0633\u0643.";
    }
}
