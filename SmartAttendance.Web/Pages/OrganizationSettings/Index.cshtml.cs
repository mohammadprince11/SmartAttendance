using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.OrganizationSettings;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "companies";

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty]
    public CompanyInputModel CompanyInput { get; set; } = new();

    [BindProperty]
    public BranchInputModel BranchInput { get; set; } = new();

    [BindProperty]
    public DepartmentInputModel DepartmentInput { get; set; } = new();

    [BindProperty]
    public PositionInputModel PositionInput { get; set; } = new();

    public List<CompanyRow> Companies { get; set; } = new();

    public List<BranchRow> Branches { get; set; } = new();

    public List<DepartmentRow> Departments { get; set; } = new();

    public List<PositionRow> Positions { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        await EnsureReadyAsync();
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostSaveCompanyAsync()
    {
        await EnsureReadyAsync();

        if (string.IsNullOrWhiteSpace(CompanyInput.Name))
        {
            ErrorMessage = "اسم الشركة مطلوب.";
            return RedirectToTab("companies");
        }

        var name = CompanyInput.Name.Trim();
        var code = NormalizeCode(CompanyInput.Code, name);

        var exists = await _dbContext.Companies
            .AnyAsync(x => x.Id != CompanyInput.Id && (x.Name == name || x.Code == code));

        if (exists)
        {
            ErrorMessage = "الشركة موجودة مسبقاً بنفس الاسم أو الكود.";
            return RedirectToTab("companies");
        }

        if (CompanyInput.Id > 0)
        {
            var company = await _dbContext.Companies.FirstOrDefaultAsync(x => x.Id == CompanyInput.Id);

            if (company == null)
            {
                ErrorMessage = "الشركة غير موجودة.";
                return RedirectToTab("companies");
            }

            company.Name = name;
            company.Code = code;
            company.IsActive = CompanyInput.IsActive;
            await _dbContext.SaveChangesAsync();

            await WriteAuditAsync("Company", CompanyInput.Id.ToString(), "Update Company", HrmsDatabase.JsonLine(("Name", name), ("Code", code), ("IsActive", CompanyInput.IsActive)));
            SuccessMessage = "تم تعديل الشركة.";
        }
        else
        {
            var company = new Company
            {
                Name = name,
                Code = code,
                IsActive = CompanyInput.IsActive
            };

            _dbContext.Companies.Add(company);
            await _dbContext.SaveChangesAsync();

            await WriteAuditAsync("Company", company.Id.ToString(), "Create Company", HrmsDatabase.JsonLine(("Name", name), ("Code", code), ("IsActive", CompanyInput.IsActive)));
            SuccessMessage = "تمت إضافة الشركة.";
        }

        return RedirectToTab("companies");
    }

    public async Task<IActionResult> OnPostSaveBranchAsync()
    {
        await EnsureReadyAsync();

        if (BranchInput.CompanyId <= 0 || string.IsNullOrWhiteSpace(BranchInput.Name))
        {
            ErrorMessage = "الشركة واسم الفرع مطلوبان.";
            return RedirectToTab("branches");
        }

        var companyExists = await _dbContext.Companies.AnyAsync(x => x.Id == BranchInput.CompanyId);
        if (!companyExists)
        {
            ErrorMessage = "الشركة المحددة غير موجودة.";
            return RedirectToTab("branches");
        }

        var name = BranchInput.Name.Trim();
        var code = NormalizeCode(BranchInput.Code, name);

        var exists = await _dbContext.Branches
            .AnyAsync(x => x.Id != BranchInput.Id && x.CompanyId == BranchInput.CompanyId && (x.Name == name || x.Code == code));

        if (exists)
        {
            ErrorMessage = "الفرع موجود مسبقاً داخل نفس الشركة بنفس الاسم أو الكود.";
            return RedirectToTab("branches");
        }

        if (BranchInput.Id > 0)
        {
            var branch = await _dbContext.Branches.FirstOrDefaultAsync(x => x.Id == BranchInput.Id);

            if (branch == null)
            {
                ErrorMessage = "الفرع غير موجود.";
                return RedirectToTab("branches");
            }

            branch.CompanyId = BranchInput.CompanyId;
            branch.Name = name;
            branch.Code = code;
            branch.Address = string.IsNullOrWhiteSpace(BranchInput.Address) ? null : BranchInput.Address.Trim();
            branch.IsActive = BranchInput.IsActive;

            await _dbContext.SaveChangesAsync();

            await WriteAuditAsync("Branch", BranchInput.Id.ToString(), "Update Branch", HrmsDatabase.JsonLine(("Name", name), ("Code", code), ("CompanyId", BranchInput.CompanyId), ("IsActive", BranchInput.IsActive)));
            SuccessMessage = "تم تعديل الفرع.";
        }
        else
        {
            var branch = new Branch
            {
                CompanyId = BranchInput.CompanyId,
                Name = name,
                Code = code,
                Address = string.IsNullOrWhiteSpace(BranchInput.Address) ? null : BranchInput.Address.Trim(),
                IsActive = BranchInput.IsActive
            };

            _dbContext.Branches.Add(branch);
            await _dbContext.SaveChangesAsync();

            await WriteAuditAsync("Branch", branch.Id.ToString(), "Create Branch", HrmsDatabase.JsonLine(("Name", name), ("Code", code), ("CompanyId", BranchInput.CompanyId), ("IsActive", BranchInput.IsActive)));
            SuccessMessage = "تمت إضافة الفرع.";
        }

        return RedirectToTab("branches");
    }

    public async Task<IActionResult> OnPostSaveDepartmentAsync()
    {
        await EnsureReadyAsync();

        if (DepartmentInput.BranchId <= 0 || string.IsNullOrWhiteSpace(DepartmentInput.Name))
        {
            ErrorMessage = "الفرع واسم القسم مطلوبان.";
            return RedirectToTab("departments");
        }

        var branchExists = await _dbContext.Branches.AnyAsync(x => x.Id == DepartmentInput.BranchId);
        if (!branchExists)
        {
            ErrorMessage = "الفرع المحدد غير موجود.";
            return RedirectToTab("departments");
        }

        var name = DepartmentInput.Name.Trim();
        var code = NormalizeCode(DepartmentInput.Code, name);

        var exists = await _dbContext.Departments
            .AnyAsync(x => x.Id != DepartmentInput.Id && x.BranchId == DepartmentInput.BranchId && (x.Name == name || x.Code == code));

        if (exists)
        {
            ErrorMessage = "القسم موجود مسبقاً داخل نفس الفرع بنفس الاسم أو الكود.";
            return RedirectToTab("departments");
        }

        if (DepartmentInput.Id > 0)
        {
            var department = await _dbContext.Departments.FirstOrDefaultAsync(x => x.Id == DepartmentInput.Id);

            if (department == null)
            {
                ErrorMessage = "القسم غير موجود.";
                return RedirectToTab("departments");
            }

            department.BranchId = DepartmentInput.BranchId;
            department.Name = name;
            department.Code = code;
            department.IsActive = DepartmentInput.IsActive;

            await _dbContext.SaveChangesAsync();

            await WriteAuditAsync("Department", DepartmentInput.Id.ToString(), "Update Department", HrmsDatabase.JsonLine(("Name", name), ("Code", code), ("BranchId", DepartmentInput.BranchId), ("IsActive", DepartmentInput.IsActive)));
            SuccessMessage = "تم تعديل القسم.";
        }
        else
        {
            var department = new Department
            {
                BranchId = DepartmentInput.BranchId,
                Name = name,
                Code = code,
                IsActive = DepartmentInput.IsActive
            };

            _dbContext.Departments.Add(department);
            await _dbContext.SaveChangesAsync();

            await WriteAuditAsync("Department", department.Id.ToString(), "Create Department", HrmsDatabase.JsonLine(("Name", name), ("Code", code), ("BranchId", DepartmentInput.BranchId), ("IsActive", DepartmentInput.IsActive)));
            SuccessMessage = "تمت إضافة القسم.";
        }

        return RedirectToTab("departments");
    }

    public async Task<IActionResult> OnPostSavePositionAsync()
    {
        await EnsureReadyAsync();

        if (string.IsNullOrWhiteSpace(PositionInput.Name))
        {
            ErrorMessage = "اسم المنصب مطلوب.";
            return RedirectToTab("positions");
        }

        var name = PositionInput.Name.Trim();
        var code = NormalizeCode(PositionInput.Code, name);

        var duplicate = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
SELECT COUNT(*)
FROM JobPositions
WHERE Id <> @Id
  AND (Code = @Code OR Name = @Name);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Id", PositionInput.Id);
                HrmsDatabase.AddParameter(command, "@Code", code);
                HrmsDatabase.AddParameter(command, "@Name", name);
            });

        if (duplicate > 0)
        {
            ErrorMessage = "المنصب موجود مسبقاً بنفس الاسم أو الكود.";
            return RedirectToTab("positions");
        }

        if (PositionInput.Id > 0)
        {
            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                """
UPDATE JobPositions
SET Code = @Code,
    Name = @Name,
    Description = @Description,
    IsActive = @IsActive,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @Id;

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('JobPosition', CAST(@Id AS nvarchar(80)), 'Update Position', @NewValues, 'HR', @IpAddress);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Id", PositionInput.Id);
                    HrmsDatabase.AddParameter(command, "@Code", code);
                    HrmsDatabase.AddParameter(command, "@Name", name);
                    HrmsDatabase.AddParameter(command, "@Description", PositionInput.Description);
                    HrmsDatabase.AddParameter(command, "@IsActive", PositionInput.IsActive);
                    HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(("Code", code), ("Name", name), ("IsActive", PositionInput.IsActive)));
                    HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
                });

            SuccessMessage = "تم تعديل المنصب.";
        }
        else
        {
            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                """
INSERT INTO JobPositions (Code, Name, Description, IsActive, CreatedAt)
VALUES (@Code, @Name, @Description, @IsActive, SYSUTCDATETIME());

DECLARE @NewId int = SCOPE_IDENTITY();

INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES ('JobPosition', CAST(@NewId AS nvarchar(80)), 'Create Position', @NewValues, 'HR', @IpAddress);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Code", code);
                    HrmsDatabase.AddParameter(command, "@Name", name);
                    HrmsDatabase.AddParameter(command, "@Description", PositionInput.Description);
                    HrmsDatabase.AddParameter(command, "@IsActive", PositionInput.IsActive);
                    HrmsDatabase.AddParameter(command, "@NewValues", HrmsDatabase.JsonLine(("Code", code), ("Name", name), ("IsActive", PositionInput.IsActive)));
                    HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
                });

            SuccessMessage = "تمت إضافة المنصب.";
        }

        return RedirectToTab("positions");
    }

    private async Task LoadAsync()
    {
        Companies = await _dbContext.Companies
            .AsNoTracking()
            .Select(x => new CompanyRow
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                IsActive = x.IsActive,
                BranchCount = x.Branches.Count
            })
            .Where(x => string.IsNullOrWhiteSpace(Search) || x.Name.Contains(Search) || x.Code.Contains(Search))
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .ToListAsync();

        Branches = await _dbContext.Branches
            .AsNoTracking()
            .Select(x => new BranchRow
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                CompanyName = x.Company.Name,
                Code = x.Code,
                Name = x.Name,
                Address = x.Address,
                IsActive = x.IsActive,
                DepartmentCount = x.Departments.Count
            })
            .Where(x => string.IsNullOrWhiteSpace(Search) || x.Name.Contains(Search) || x.Code.Contains(Search) || x.CompanyName.Contains(Search))
            .OrderBy(x => x.CompanyName)
            .ThenBy(x => x.Name)
            .ToListAsync();

        Departments = await _dbContext.Departments
            .AsNoTracking()
            .Select(x => new DepartmentRow
            {
                Id = x.Id,
                BranchId = x.BranchId,
                BranchName = x.Branch.Name,
                CompanyName = x.Branch.Company.Name,
                Code = x.Code,
                Name = x.Name,
                IsActive = x.IsActive,
                EmployeeCount = x.Employees.Count
            })
            .Where(x => string.IsNullOrWhiteSpace(Search) || x.Name.Contains(Search) || x.Code.Contains(Search) || x.BranchName.Contains(Search) || x.CompanyName.Contains(Search))
            .OrderBy(x => x.CompanyName)
            .ThenBy(x => x.BranchName)
            .ThenBy(x => x.Name)
            .ToListAsync();

        Positions = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT
    Id,
    Code,
    Name,
    ISNULL(Description, '') AS Description,
    IsActive,
    CreatedAt,
    UpdatedAt
FROM JobPositions
WHERE
    (@Search IS NULL OR @Search = ''
     OR Code LIKE '%' + @Search + '%'
     OR Name LIKE '%' + @Search + '%'
     OR ISNULL(Description, '') LIKE '%' + @Search + '%')
ORDER BY IsActive DESC, Name;
""",
            command => HrmsDatabase.AddParameter(command, "@Search", Search),
            reader => new PositionRow
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Code = HrmsDatabase.GetString(reader, "Code"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                Description = HrmsDatabase.GetString(reader, "Description"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt"),
                UpdatedAt = HrmsDatabase.GetDateTime(reader, "UpdatedAt")
            });
    }

    private async Task EnsureReadyAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF OBJECT_ID('JobPositions', 'U') IS NULL
BEGIN
    CREATE TABLE JobPositions
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Code nvarchar(50) NOT NULL,
        Name nvarchar(150) NOT NULL,
        Description nvarchar(500) NULL,
        IsActive bit NOT NULL DEFAULT(1),
        CreatedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        UpdatedAt datetime2 NULL
    );

    CREATE UNIQUE INDEX IX_JobPositions_Code ON JobPositions(Code);
    CREATE UNIQUE INDEX IX_JobPositions_Name ON JobPositions(Name);
END;
""");
    }

    private async Task WriteAuditAsync(string entityName, string entityId, string action, string values)
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
INSERT INTO AuditLogs (EntityName, EntityId, Action, NewValues, UserName, IpAddress)
VALUES (@EntityName, @EntityId, @Action, @NewValues, 'HR', @IpAddress);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EntityName", entityName);
                HrmsDatabase.AddParameter(command, "@EntityId", entityId);
                HrmsDatabase.AddParameter(command, "@Action", action);
                HrmsDatabase.AddParameter(command, "@NewValues", values);
                HrmsDatabase.AddParameter(command, "@IpAddress", HttpContext.Connection.RemoteIpAddress?.ToString());
            });
    }

    private IActionResult RedirectToTab(string tab)
    {
        return RedirectToPage(new { Tab = tab });
    }

    private static string NormalizeCode(string? code, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            return code.Trim();
        }

        var value = new string(fallback
            .Where(char.IsLetterOrDigit)
            .Take(12)
            .ToArray());

        return string.IsNullOrWhiteSpace(value)
            ? Guid.NewGuid().ToString("N")[..8]
            : value.ToUpperInvariant();
    }

    public class CompanyInputModel
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public class BranchInputModel
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string? Code { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class DepartmentInputModel
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string? Code { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }

    public class PositionInputModel
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class CompanyRow
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int BranchCount { get; set; }
    }

    public class BranchRow
    {
        public int Id { get; set; }
        public int CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public bool IsActive { get; set; }
        public int DepartmentCount { get; set; }
    }

    public class DepartmentRow
    {
        public int Id { get; set; }
        public int BranchId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int EmployeeCount { get; set; }
    }

    public class PositionRow
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
