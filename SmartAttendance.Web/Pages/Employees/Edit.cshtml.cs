using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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

    public List<string> ReligionOptions { get; set; } = new();
    public List<string> WorkTypeOptions { get; set; } = new();
    public List<string> GradeOptions { get; set; } = new();
    public List<string> SponsorOptions { get; set; } = new();

    /// <summary>الحقول الإلزامية من «استوديو الحقول» — تعلَّم بنجمة وتُفرض بالسيرفر.</summary>
    public HashSet<string> RequiredFieldKeys { get; set; } = new();

    /// <summary>إعدادات الحقول الكاملة (إخفاء/تسمية/ترتيب) — تطبّقها الواجهة.</summary>
    public Dictionary<string, EmployeeFieldControl.FieldSetting> FieldSettings { get; set; } = new();

    private async Task LoadLookupsAsync()
    {
        ReligionOptions = await HrLookups.ValuesAsync(_dbContext, "religions");
        WorkTypeOptions = await HrLookups.ValuesAsync(_dbContext, "worktypes");
        GradeOptions = await HrLookups.ValuesAsync(_dbContext, "grades");
        SponsorOptions = await HrLookups.ValuesAsync(_dbContext, "sponsors");
    }

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
        PrefillQuadNameFromFullName();

        PositionOptions = await _employeeService.GetPositionsForDropdownAsync();
        CurrentPhotoPath = await GetEmployeePhotoPathAsync(Employee.Id);
        ProfileDynamicSections = await EmployeeProfileDynamicFields.LoadSectionsAsync(_dbContext, Employee.Id);
        Managers = await LoadManagersAsync(Employee.Id);
        DirectManagerId = Employee.DirectManagerId;
        await LoadLookupsAsync();
        FieldSettings = await EmployeeFieldControl.GetSettingsAsync(_dbContext);
        RequiredFieldKeys = EmployeeFieldControl.RequiredKeys(FieldSettings);

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
        await LoadLookupsAsync();
        FieldSettings = await EmployeeFieldControl.GetSettingsAsync(_dbContext);
        RequiredFieldKeys = EmployeeFieldControl.RequiredKeys(FieldSettings);

        // التحكم بالحقول: فرض الإلزامية المركزية بالسيرفر.
        EmployeeFieldControl.ValidateRequired(Employee, RequiredFieldKeys, ModelState, "Employee");

        if (!ModelState.IsValid) return Page();

        if (DirectManagerId.HasValue && DirectManagerId.Value == Employee.Id)
        {
            ErrorMessage = "لا يمكن تعيين الموظف مديراً مباشراً لنفسه.";
            return Page();
        }

        Employee.DirectManagerId = DirectManagerId;

        // بيانات الموظف والمدير المباشر والحقول الديناميكية تُحفظ ضمن معاملة واحدة؛
        // حفظ الصورة (عملية ملفات) يبقى خارجها حتى لا يؤثر فشله على البيانات.
        await using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            var updated = await _employeeService.UpdateAsync(Employee);

            if (!updated)
            {
                ErrorMessage = "تعذر حفظ التعديل. تأكد من كود الموظف وموقع العمل والقسم والمدير المباشر.";
                return Page();
            }

            await EmployeeProfileDynamicFields.SaveAsync(_dbContext, Employee.Id, Request.Form);

            await transaction.CommitAsync();
        }

        var photoResult = await SaveEmployeePhotoAsync(Employee.Id);

        TempData["SuccessMessage"] = string.IsNullOrWhiteSpace(photoResult)
            ? "تم تحديث بيانات الموظف بنجاح."
            : $"تم تحديث بيانات الموظف بنجاح. {photoResult}";

        return RedirectToPage("./Profile", new { id = Employee.Id });
    }

    // الموظفون القدامى عندهم FullName فقط؛ نوزّعه على خانات الرباعي حتى يبقى
    // مصدر إدخال الاسم واحداً بالنموذج، وإعادة تركيبه عند الحفظ تعطي نفس النص.
    private void PrefillQuadNameFromFullName()
    {
        var hasQuad = !string.IsNullOrWhiteSpace(Employee.FirstName) ||
                      !string.IsNullOrWhiteSpace(Employee.SecondName) ||
                      !string.IsNullOrWhiteSpace(Employee.ThirdName) ||
                      !string.IsNullOrWhiteSpace(Employee.LastName);

        if (hasQuad || string.IsNullOrWhiteSpace(Employee.FullName))
        {
            return;
        }

        var parts = Employee.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Employee.FirstName = parts.Length > 0 ? parts[0] : null;
        Employee.LastName = parts.Length > 1 ? parts[^1] : null;
        Employee.SecondName = parts.Length > 2 ? parts[1] : null;
        Employee.ThirdName = parts.Length > 3 ? string.Join(' ', parts[2..^1]) : null;
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
        return await _dbContext.Employees
            .AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => x.PhotoPath ?? string.Empty)
            .FirstOrDefaultAsync() ?? string.Empty;
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
        await _dbContext.Employees
            .Where(x => x.Id == employeeId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.PhotoPath, relativePath));
        CurrentPhotoPath = relativePath;
        return "تم حفظ الصورة.";
    }
}
