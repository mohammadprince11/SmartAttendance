using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Employees;

/// <summary>
/// Edit an employee's financial / payroll setup (Kayan "المعلومات المالية").
/// One row per employee (upsert). Access follows the same People edit permission
/// as the profile Edit page.
/// </summary>
public class FinancialInfoModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly IPermissionAuthorizationService _permissionAuthorizationService;

    public FinancialInfoModel(
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        IPermissionAuthorizationService permissionAuthorizationService)
    {
        _dbContext = dbContext;
        _environment = environment;
        _permissionAuthorizationService = permissionAuthorizationService;
    }

    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string EmployeeNo { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    /// <summary>مجموع العلاوات النشطة اليوم — لعرض «الراتب الكلي = أساسي + علاوات» حياً.</summary>
    public decimal ActiveAllowancesTotal { get; set; }

    [BindProperty] public EmployeeFinancialInfo Input { get; set; } = new();
    [BindProperty] public IFormFile? CommitmentFile { get; set; }
    [BindProperty] public IFormFile? Attachment { get; set; }

    public static readonly string[] Currencies = { "IQD", "USD", "EUR", "SAR", "AED", "JOD", "EGP", "KWD", "BHD", "QAR", "OMR" };
    public static readonly string[] PaymentMethods = { "نقداً", "شيك", "تحويل بنكي" };

    /// <summary>الحقول الحساسة: الصفحة كاملة راتب — تُحجب عن الأدوار غير المخوّلة.</summary>
    private async Task<bool> CanViewSalaryAsync()
    {
        var allowedRoles = await SmartAttendance.Web.Infrastructure.HrSettings.HrSettingsStore.GetAsync(
            _dbContext, "Sensitive.SalaryRoles", "Admin,HR Manager");
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? string.Empty;
        return allowedRoles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await CanViewSalaryAsync()) return Forbid();
        if (!await CanEditAsync(id)) return Forbid();
        await EmployeeFinancialInfoSchema.EnsureAsync(_dbContext);

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
        if (employee == null) return NotFound();

        EmployeeId = employee.Id;
        EmployeeName = employee.FullName;
        EmployeeNo = employee.EmployeeNo;

        Input = await _dbContext.EmployeeFinancialInfos.AsNoTracking()
            .FirstOrDefaultAsync(f => f.EmployeeId == id)
            ?? new EmployeeFinancialInfo { EmployeeId = id };

        await EmployeeAllowanceSchema.EnsureAsync(_dbContext);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var allowances = await _dbContext.EmployeeAllowances.AsNoTracking()
            .Where(a => a.EmployeeId == id)
            .ToListAsync();
        ActiveAllowancesTotal = allowances.Where(a => a.IsActiveOn(today)).Sum(a => a.Amount);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await CanViewSalaryAsync()) return Forbid();
        if (!await CanEditAsync(id)) return Forbid();
        await EmployeeFinancialInfoSchema.EnsureAsync(_dbContext);

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted);
        if (employee == null) return NotFound();

        EmployeeId = employee.Id;
        EmployeeName = employee.FullName;
        EmployeeNo = employee.EmployeeNo;

        var user = User.Identity?.Name ?? "System";
        var now = DateTime.UtcNow;

        var entity = await _dbContext.EmployeeFinancialInfos.FirstOrDefaultAsync(f => f.EmployeeId == id);
        if (entity == null)
        {
            entity = new EmployeeFinancialInfo { EmployeeId = id, CreatedAt = now, CreatedBy = user };
            _dbContext.EmployeeFinancialInfos.Add(entity);
        }
        else
        {
            entity.UpdatedAt = now;
            entity.UpdatedBy = user;
        }

        // Salary & currency
        entity.Currency = Clean(Input.Currency);
        entity.SalaryScale = Clean(Input.SalaryScale);
        entity.BasicSalary = Input.BasicSalary;
        entity.DailySalary = Input.DailySalary;
        entity.HourlyRate = Input.HourlyRate;
        // Social security
        entity.SocialSecurityType = Clean(Input.SocialSecurityType);
        entity.SocialSecuritySalary = Input.SocialSecuritySalary;
        entity.SocialSecurityNo = Clean(Input.SocialSecurityNo);
        entity.SocialSecurityJoinDate = Input.SocialSecurityJoinDate;
        entity.SocialSecurityPreviousMonths = Input.SocialSecurityPreviousMonths;
        entity.RetirementAge = Input.RetirementAge;
        // Tax
        entity.TaxFile = Clean(Input.TaxFile);
        entity.TaxNo = Clean(Input.TaxNo);
        entity.TaxYear = Input.TaxYear;
        entity.PreviousTaxSalary = Input.PreviousTaxSalary;
        entity.PreviousTaxExemption = Input.PreviousTaxExemption;
        entity.PreviousTaxAmount = Input.PreviousTaxAmount;
        entity.PreviousMinSalary = Input.PreviousMinSalary;
        entity.PreviousMinTaxAmount = Input.PreviousMinTaxAmount;
        entity.PreviousTaxMonths = Input.PreviousTaxMonths;
        // End of service
        entity.EndOfServiceSetup = Clean(Input.EndOfServiceSetup);
        entity.EndOfServiceCompute = Input.EndOfServiceCompute;
        entity.EndOfServiceStartDate = Input.EndOfServiceStartDate;
        entity.EndOfServiceDueDate = Input.EndOfServiceDueDate;
        // Calc control
        entity.CalcPreviousSalaries = Input.CalcPreviousSalaries;
        entity.StopSalaryCalc = Input.StopSalaryCalc;
        entity.AdditionalSalaryStartDate = Input.AdditionalSalaryStartDate;
        // Payment & bank
        entity.PaymentMethod = Clean(Input.PaymentMethod);
        entity.BankName = Clean(Input.BankName);
        entity.BankBranch = Clean(Input.BankBranch);
        entity.UnitNo = Clean(Input.UnitNo);
        entity.Iban = Clean(Input.Iban);
        entity.CardNo = Clean(Input.CardNo);
        entity.MxpAccount = Clean(Input.MxpAccount);
        entity.BankCommitment = Input.BankCommitment;

        var commitment = await SaveFileAsync(CommitmentFile, id);
        if (commitment.path != null) { entity.BankCommitmentFileName = commitment.name; entity.BankCommitmentFilePath = commitment.path; }
        var attach = await SaveFileAsync(Attachment, id);
        if (attach.path != null) { entity.AttachmentName = attach.name; entity.AttachmentPath = attach.path; }

        await _dbContext.SaveChangesAsync();
        TempData["SuccessMessage"] = "تم حفظ المعلومات المالية.";
        return RedirectToPage("./Profile", new { id });
    }

    private Task<bool> CanEditAsync(int employeeId)
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

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static readonly string[] AllowedExtensions = { ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".doc", ".docx", ".xls", ".xlsx" };

    private async Task<(string? name, string? path)> SaveFileAsync(IFormFile? file, int employeeId)
    {
        if (file == null || file.Length == 0) return (null, null);
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return (null, null);
        if (file.Length > 10 * 1024 * 1024) return (null, null);

        var root = Path.Combine(_environment.WebRootPath, "uploads", "employee-financial");
        Directory.CreateDirectory(root);
        var fileName = $"fin_{employeeId}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext.ToLowerInvariant()}";
        await using (var stream = System.IO.File.Create(Path.Combine(root, fileName)))
        {
            await file.CopyToAsync(stream);
        }
        return (Path.GetFileName(file.FileName), $"/uploads/employee-financial/{fileName}");
    }
}
