using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
    public List<string> InitialDocumentTypes { get; set; } = new();

    [BindProperty]
    public List<string> InitialDocumentRequired { get; set; } = new();

    [BindProperty]
    public List<IFormFile> InitialDocumentFiles { get; set; } = new();

    public IEnumerable<DepartmentListViewModel> Departments { get; set; } = new List<DepartmentListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();

        if (!ModelState.IsValid)
            return Page();

        var created = await _employeeService.CreateAsync(Employee);

        if (!created)
        {
            ErrorMessage = "كود الموظف موجود مسبقاً أو القسم المحدد غير صحيح.";
            return Page();
        }

        var employeeId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            "SELECT TOP 1 Id FROM Employees WHERE EmployeeNo = @EmployeeNo ORDER BY Id DESC",
            command => HrmsDatabase.AddParameter(command, "@EmployeeNo", Employee.EmployeeNo));

        if (employeeId > 0)
        {
            var documentResult = await SaveInitialDocumentsAsync(employeeId);

            if (!string.IsNullOrWhiteSpace(documentResult))
            {
                TempData["SuccessMessage"] = $"تم إنشاء الموظف بنجاح. {documentResult}";
            }
            else
            {
                TempData["SuccessMessage"] = "تم إنشاء الموظف بنجاح.";
            }
        }
        else
        {
            TempData["SuccessMessage"] = "تم إنشاء الموظف بنجاح.";
        }

        return RedirectToPage("./Index");
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
            return skippedCount > 0 ? "لم يتم حفظ أي مستمسك لأن الملفات غير صالحة أو فارغة." : string.Empty;

        return skippedCount > 0
            ? $"تم حفظ {savedCount} مستمسك، وتم تجاوز {skippedCount} ملف غير صالح."
            : $"تم حفظ {savedCount} مستمسك.";
    }
}
