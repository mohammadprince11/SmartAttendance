using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

public class EditModel : PageModel
{
    private readonly IEmployeeService _employeeService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly IPermissionAuthorizationService _permissionAuthorizationService;

    private static readonly HashSet<string> AllowedEmployeePhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    public EditModel(
        IEmployeeService employeeService,
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        IPermissionAuthorizationService permissionAuthorizationService)
    {
        _employeeService = employeeService;
        _dbContext = dbContext;
        _environment = environment;
        _permissionAuthorizationService = permissionAuthorizationService;
    }

    [BindProperty]
    public EmployeeEditViewModel Employee { get; set; } = new();

    [BindProperty]
    public IFormFile? EmployeePhoto { get; set; }

    [BindProperty]
    public int? DirectManagerId { get; set; }

    public class ManagerOption { 
        public int Id { get; set; } 
        public string EmployeeNo { get; set; } = string.Empty; 
        public string FullName { get; set; } = string.Empty; 
    }
    public IEnumerable<ManagerOption> Managers { get; set; } = new List<ManagerOption>();

    public string CurrentPhotoPath { get; set; } = string.Empty;
    public IEnumerable<BranchListViewModel> Branches { get; set; } = new List<BranchListViewModel>();
    public IEnumerable<DepartmentListViewModel> Departments { get; set; } = new List<DepartmentListViewModel>();
    public IEnumerable<PositionOptionViewModel> PositionOptions { get; set; } = new List<PositionOptionViewModel>();
    public List<EmployeeProfileDynamicSection> ProfileDynamicSections { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        // Defense-in-depth: نفس فحص صلاحية التعديل الموجود بصفحة Profile،
        // حتى لا يكون الرابط المباشر للصفحة كافياً لتجاوز الصلاحيات.
        if (!await CanEditEmployeeAsync(id))
        {
            return Forbid();
        }

        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Branches = await _employeeService.GetBranchesForDropdownAsync();
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();

        var employee = await _employeeService.GetEditByIdAsync(id);

        if (employee == null) return NotFound();

        Employee = employee;

        PositionOptions = await _employeeService.GetPositionsForDropdownAsync();
        CurrentPhotoPath = await GetEmployeePhotoPathAsync(Employee.Id);
        ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, Employee.Id);
        Managers = await LoadManagersAsync(Employee.Id);

        // --- الإصلاح الجذري لمشكلة الـ Cast Exception ---
        var managerObj = await HrmsDatabase.ScalarAsync<object>(_dbContext, "SELECT DirectManagerId FROM Employees WHERE Id = @Id", cmd => HrmsDatabase.AddParameter(cmd, "@Id", id));
        DirectManagerId = (managerObj == null || managerObj == DBNull.Value) ? null : Convert.ToInt32(managerObj);
        // ------------------------------------------------

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await CanEditEmployeeAsync(Employee.Id))
        {
            return Forbid();
        }

        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        Branches = await _employeeService.GetBranchesForDropdownAsync();
        Departments = await _employeeService.GetDepartmentsForDropdownAsync();
        PositionOptions = await _employeeService.GetPositionsForDropdownAsync();
        CurrentPhotoPath = Employee.Id > 0 ? await GetEmployeePhotoPathAsync(Employee.Id) : string.Empty;
        ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, Employee.Id > 0 ? Employee.Id : 0);

        Managers = await LoadManagersAsync(Employee.Id);

        if (!ModelState.IsValid) return Page();

        if (DirectManagerId.HasValue && DirectManagerId.Value == Employee.Id)
        {
            ErrorMessage = "لا يمكن تعيين الموظف مديراً مباشراً لنفسه.";
            return Page();
        }

        // بيانات الموظف والمدير المباشر والحقول الديناميكية تُحفظ ضمن معاملة واحدة؛
        // حفظ الصورة (عملية ملفات) يبقى خارجها حتى لا يؤثر فشله على البيانات.
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            var updated = await _employeeService.UpdateAsync(Employee);

            if (!updated)
            {
                ErrorMessage = "تعذر حفظ التعديل. تأكد من كود الموظف وموقع العمل والقسم.";
                return Page();
            }

            await HrmsDatabase.ExecuteAsync(_dbContext, "UPDATE Employees SET DirectManagerId = @ManagerId WHERE Id = @Id", cmd => {
                HrmsDatabase.AddParameter(cmd, "@ManagerId", DirectManagerId.HasValue ? (object)DirectManagerId.Value : DBNull.Value);
                HrmsDatabase.AddParameter(cmd, "@Id", Employee.Id);
            });

            await EmployeeProfileDynamicFields.SaveAsync(_dbContext, Employee.Id, Request.Form);

            await transaction.CommitAsync();
        }

        var photoResult = await SaveEmployeePhotoAsync(Employee.Id);

        TempData["SuccessMessage"] = string.IsNullOrWhiteSpace(photoResult)
            ? "تم تحديث بيانات الموظف بنجاح."
            : $"تم تحديث بيانات الموظف بنجاح. {photoResult}";

        return RedirectToPage("./Profile", new { id = Employee.Id });
    }

    private Task<bool> CanEditEmployeeAsync(int employeeId)
    {
        var systemUserId = PeopleAccessContext.GetSystemUserId(HttpContext) ?? 0;
        var role = PeopleAccessContext.GetRole(HttpContext);

        return _permissionAuthorizationService.CanAccessEmployeeAsync(
            systemUserId,
            PeoplePermissionCodes.Edit,
            employeeId,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.Edit),
            HttpContext.RequestAborted);
    }

    private async Task<IEnumerable<ManagerOption>> LoadManagersAsync(int employeeId)
    {
        // القائمة محصورة بموظفي نفس شركة الموظف الحالي، مع استثنائه من الخيارات.
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
            SELECT e.Id, ISNULL(e.EmployeeNo, '') AS EmployeeNo, e.FullName
            FROM Employees e
            INNER JOIN Branches b ON e.BranchId = b.Id
            WHERE e.IsActive = 1
              AND e.IsDeleted = 0
              AND e.Id <> @SelfId
              AND b.CompanyId = (
                  SELECT b2.CompanyId
                  FROM Employees e2
                  INNER JOIN Branches b2 ON e2.BranchId = b2.Id
                  WHERE e2.Id = @SelfId
              )
            ORDER BY e.FullName;
            """,
            cmd => HrmsDatabase.AddParameter(cmd, "@SelfId", employeeId),
            reader => new ManagerOption { Id = Convert.ToInt32(reader["Id"]), EmployeeNo = reader["EmployeeNo"].ToString() ?? "", FullName = reader["FullName"].ToString() ?? "" });
    }

    private async Task<string> GetEmployeePhotoPathAsync(int employeeId)
    {
        return await HrmsDatabase.ScalarAsync<string>(_dbContext, "SELECT ISNULL(PhotoPath, '') FROM Employees WHERE Id = @EmployeeId", command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId)) ?? string.Empty;
    }

    private async Task<string> SaveEmployeePhotoAsync(int employeeId)
    {
        if (EmployeePhoto == null || EmployeePhoto.Length == 0) return string.Empty;
        var extension = Path.GetExtension(EmployeePhoto.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedEmployeePhotoExtensions.Contains(extension)) return "صيغة الصورة غير مدعومة.";
        if (EmployeePhoto.Length > 5 * 1024 * 1024) return "حجم الصورة أكبر من 5MB.";
        if (!await UploadSignatureValidator.IsValidImageAsync(EmployeePhoto)) return "محتوى الملف ليس صورة صالحة.";

        var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "employee-photos");
        Directory.CreateDirectory(uploadRoot);
        var storedName = $"employee_{employeeId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        var physicalPath = Path.Combine(uploadRoot, storedName);
        var relativePath = $"/uploads/employee-photos/{storedName}";

        await using (var stream = System.IO.File.Create(physicalPath)) { await EmployeePhoto.CopyToAsync(stream); }
        await HrmsDatabase.ExecuteAsync(_dbContext, "UPDATE Employees SET PhotoPath = @PhotoPath WHERE Id = @EmployeeId;", command => { HrmsDatabase.AddParameter(command, "@PhotoPath", relativePath); HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId); });
        CurrentPhotoPath = relativePath;
        return "تم حفظ الصورة.";
    }
}
