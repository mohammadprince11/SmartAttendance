using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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

    public IEnumerable<DepartmentListViewModel> Departments { get; set; } = new List<DepartmentListViewModel>();

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();

        var employee = await _employeeService.GetEditByIdAsync(id);

        if (employee == null)
            return NotFound();

        Employee = employee;
        CurrentPhotoPath = await GetEmployeePhotoPathAsync(Employee.Id);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();
        CurrentPhotoPath = Employee.Id > 0 ? await GetEmployeePhotoPathAsync(Employee.Id) : string.Empty;

        if (!ModelState.IsValid)
            return Page();

        var updated = await _employeeService.UpdateAsync(Employee);

        if (!updated)
        {
            ErrorMessage = "تعذر حفظ التعديل. تأكد من كود الموظف والقسم المحدد.";
            return Page();
        }

        var photoResult = await SaveEmployeePhotoAsync(Employee.Id);

        TempData["SuccessMessage"] = string.IsNullOrWhiteSpace(photoResult)
            ? "تم تحديث بيانات الموظف بنجاح."
            : $"تم تحديث بيانات الموظف بنجاح. {photoResult}";

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
            return "لم يتم حفظ صورة الموظف لأن الصيغة غير مدعومة.";

        if (EmployeePhoto.Length > 5 * 1024 * 1024)
            return "لم يتم حفظ صورة الموظف لأن حجمها أكبر من 5MB.";

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
        return "تم حفظ صورة الموظف.";
    }
}
