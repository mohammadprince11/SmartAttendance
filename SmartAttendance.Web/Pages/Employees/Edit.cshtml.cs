using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Employees;

public class EditModel : PageModel
{
    private readonly IEmployeeService _employeeService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    private static readonly HashSet<string> AllowedEmployeePhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    public EditModel(
        IEmployeeService employeeService,
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment)
    {
        _employeeService = employeeService;
        _dbContext = dbContext;
        _environment = environment;
    }

    [BindProperty]
    public EmployeeEditViewModel Employee { get; set; } = new();

    [BindProperty]
    public IFormFile? EmployeePhoto { get; set; }

    public string CurrentPhotoPath { get; set; } = string.Empty;

    public IEnumerable<BranchListViewModel> Branches { get; set; } = new List<BranchListViewModel>();

    public IEnumerable<DepartmentListViewModel> Departments { get; set; } = new List<DepartmentListViewModel>();


    public IEnumerable<PositionOptionViewModel> PositionOptions { get; set; } = new List<PositionOptionViewModel>();

    public List<EmployeeProfileDynamicSection> ProfileDynamicSections { get; set; } = new();
public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Branches = await _employeeService.GetBranchesForDropdownAsync();
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();

        var employee = await _employeeService.GetEditByIdAsync(id);

        if (employee == null)
            return NotFound();

        Employee = employee;

        PositionOptions = await _employeeService.GetPositionsForDropdownAsync();
CurrentPhotoPath = await GetEmployeePhotoPathAsync(Employee.Id);
        ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, Employee.Id);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Branches = await _employeeService.GetBranchesForDropdownAsync();
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();
        CurrentPhotoPath = Employee.Id > 0 ? await GetEmployeePhotoPathAsync(Employee.Id) : string.Empty;
        ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, Employee.Id > 0 ? Employee.Id : 0);

        if (!ModelState.IsValid)
            return Page();

        var updated = await _employeeService.UpdateAsync(Employee);

        if (!updated)
        {
            ErrorMessage = "\u062a\u0639\u0630\u0631 \u062d\u0641\u0638 \u0627\u0644\u062a\u0639\u062f\u064a\u0644. \u062a\u0623\u0643\u062f \u0645\u0646 \u0643\u0648\u062f \u0627\u0644\u0645\u0648\u0638\u0641 \u0648\u0645\u0648\u0642\u0639 \u0627\u0644\u0639\u0645\u0644 \u0648\u0627\u0644\u0642\u0633\u0645 \u0648\u0623\u0646\u0647\u0627 \u062a\u062a\u0628\u0639 \u0625\u0644\u0649 \u0646\u0641\u0633 \u0627\u0644\u0634\u0631\u0643\u0629.";
            return Page();
        }

        var photoResult = await SaveEmployeePhotoAsync(Employee.Id);
        await EmployeeProfileDynamicFields.SaveAsync(_dbContext, Employee.Id, Request.Form);

        TempData["SuccessMessage"] = string.IsNullOrWhiteSpace(photoResult)
            ? "\u062a\u0645 \u062a\u062d\u062f\u064a\u062b \u0628\u064a\u0627\u0646\u0627\u062a \u0627\u0644\u0645\u0648\u0638\u0641 \u0628\u0646\u062c\u0627\u062d."
            : $"\u062a\u0645 \u062a\u062d\u062f\u064a\u062b \u0628\u064a\u0627\u0646\u0627\u062a \u0627\u0644\u0645\u0648\u0638\u0641 \u0628\u0646\u062c\u0627\u062d. {photoResult}";

        return RedirectToPage("./Profile", new { id = Employee.Id });
    }


    private async Task<string> GetEmployeePhotoPathAsync(int employeeId)
    {
        return await HrmsDatabase.ScalarAsync<string>(
            _dbContext,
            "SELECT ISNULL(PhotoPath, '') FROM Employees WHERE Id = @EmployeeId",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId)) ?? string.Empty;
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

        CurrentPhotoPath = relativePath;
        return "\u062a\u0645 \u062d\u0641\u0638 \u0635\u0648\u0631\u0629 \u0627\u0644\u0645\u0648\u0638\u0641.";
    }
}
