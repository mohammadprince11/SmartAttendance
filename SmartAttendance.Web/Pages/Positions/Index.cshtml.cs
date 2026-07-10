using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Pages.Positions;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public JobPositionForm Input { get; set; } = new();

    [BindProperty]
    public PositionReferenceForm ReferenceInput { get; set; } = new();

    public List<JobPositionRow> Positions { get; set; } = new();

    public List<DepartmentOption> Departments { get; set; } = new();

    public List<NameOption> Categories { get; set; } = new();

    public List<NameOption> Levels { get; set; } = new();

    public List<NameOption> CompetencyOptions { get; set; } = new();

    public List<NameOption> EducationOptions { get; set; } = new();

    public List<NameOption> EducationSpecializationOptions { get; set; } = new();

    public List<NameOption> CertificationOptions { get; set; } = new();

    public int TotalPositions { get; set; }

    public int ActivePositions { get; set; }

    public int LinkedPositions { get; set; }

    public int TotalLinkedEmployees { get; set; }

    public async Task OnGetAsync(int? editId)
    {
        await SmartAttendance.Web.Infrastructure.Hrms.PositionSchema.EnsureAsync(_db);
                await EnsurePositionProfileColumnsAsync();
        await EnsurePositionReferenceTablesAsync();
await SyncLookupTablesAsync();
        await SyncEmployeePositionsAsync();
        await LoadPageAsync(editId);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await SmartAttendance.Web.Infrastructure.Hrms.PositionSchema.EnsureAsync(_db);
                await EnsurePositionProfileColumnsAsync();
        await EnsurePositionReferenceTablesAsync();
await SyncLookupTablesAsync();

        Input.ArabicName = NormalizeText(Input.ArabicName);
        Input.OriginalArabicName = NormalizeText(Input.OriginalArabicName);
        Input.Category = NormalizeText(Input.Category);
        Input.Level = NormalizeText(Input.Level);
        Input.Description = NormalizeText(Input.Description);
        Input.JobPurpose = NormalizeText(Input.JobPurpose);
        Input.KeyResponsibilities = NormalizeText(Input.KeyResponsibilities);
        Input.JobRequirements = NormalizeText(Input.JobRequirements);
        Input.RequiredSkills = NormalizeText(Input.RequiredSkills);
        Input.JobKpis = NormalizeText(Input.JobKpis);
        Input.Competencies = NormalizeText(Input.Competencies);
        Input.Education = NormalizeText(Input.Education);
        Input.EducationSpecialization = NormalizeText(Input.EducationSpecialization);
        Input.Certifications = NormalizeText(Input.Certifications);

        if (string.IsNullOrWhiteSpace(Input.ArabicName))
        {
            TempData["ErrorMessage"] = "اسم المنصب مطلوب.";
            await LoadPageAsync(Input.Id > 0 ? Input.Id : null);
            return Page();
        }

        var duplicateCount = Convert.ToInt32(await ExecuteScalarAsync(@"
SELECT COUNT(1)
FROM dbo.HrJobPositions
WHERE LTRIM(RTRIM(ArabicName)) = @ArabicName
  AND Id <> @Id;
", command =>
        {
            AddParameter(command, "@ArabicName", Input.ArabicName);
            AddParameter(command, "@Id", Input.Id);
        }) ?? 0);

        if (duplicateCount > 0)
        {
            TempData["ErrorMessage"] = "اسم المنصب موجود مسبقاً.";
            await LoadPageAsync(Input.Id > 0 ? Input.Id : null);
            return Page();
        }

        if (Input.Id > 0)
        {
            var oldName = await GetPositionNameAsync(Input.Id);
            if (string.IsNullOrWhiteSpace(oldName))
            {
                TempData["ErrorMessage"] = "المنصب المطلوب تعديله غير موجود.";
                return RedirectToPage();
            }

            await ExecuteNonQueryAsync(@"
UPDATE dbo.HrJobPositions
SET ArabicName = @ArabicName,
    DepartmentId = @DepartmentId,
    Category = @Category,
    Level = @Level,
    Description = @Description,
    IsActive = @IsActive,
    UpdatedAt = SYSDATETIME()
WHERE Id = @Id;
", command =>
            {
                AddParameter(command, "@ArabicName", Input.ArabicName);
                AddParameter(command, "@DepartmentId", Input.DepartmentId);
                AddParameter(command, "@Category", EmptyToNull(Input.Category));
                AddParameter(command, "@Level", EmptyToNull(Input.Level));
                AddParameter(command, "@Description", EmptyToNull(Input.Description));
                AddParameter(command, "@IsActive", Input.IsActive);
                AddParameter(command, "@Id", Input.Id);
            });

            if (!string.Equals(NormalizeText(oldName), Input.ArabicName, StringComparison.OrdinalIgnoreCase))
            {
                await UpdateEmployeesPositionNameAsync(oldName, Input.ArabicName);
            }

            TempData["SuccessMessage"] = "تم تعديل المنصب بنجاح.";
        }
        else
        {
            await ExecuteNonQueryAsync(@"
INSERT INTO dbo.HrJobPositions
(
    ArabicName,
    EnglishName,
    JobCode,
    DepartmentId,
    Grade,
    Category,
    Level,
    Description,
    IsActive,
    CreatedAt
)
VALUES
(
    @ArabicName,
    NULL,
    NULL,
    @DepartmentId,
    NULL,
    @Category,
    @Level,
    @Description,
    1,
    SYSDATETIME()
);
", command =>
            {
                AddParameter(command, "@ArabicName", Input.ArabicName);
                AddParameter(command, "@DepartmentId", Input.DepartmentId);
                AddParameter(command, "@Category", EmptyToNull(Input.Category));
                AddParameter(command, "@Level", EmptyToNull(Input.Level));
                AddParameter(command, "@Description", EmptyToNull(Input.Description));
            });

            TempData["SuccessMessage"] = "تم حفظ المنصب بنجاح.";
        }        await SavePositionProfileAsync(Input.Id, Input.ArabicName);


        return RedirectToPage();
    }


    public async Task<IActionResult> OnPostSaveReferenceAsync(string type)
    {
        await SmartAttendance.Web.Infrastructure.Hrms.PositionSchema.EnsureAsync(_db);
        await EnsurePositionProfileColumnsAsync();
        await EnsurePositionReferenceTablesAsync();

        var tableName = ResolvePositionReferenceTable(type);
        ReferenceInput.Name = NormalizeText(ReferenceInput.Name);

        if (string.IsNullOrWhiteSpace(tableName))
        {
            TempData["ErrorMessage"] = "Invalid reference type.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(ReferenceInput.Name))
        {
            TempData["ErrorMessage"] = "Reference name is required.";
            return RedirectToPage();
        }

        await ExecuteNonQueryAsync($@"
IF EXISTS
(
    SELECT 1
    FROM {tableName}
    WHERE LTRIM(RTRIM(Name)) = @Name
)
BEGIN
    UPDATE {tableName}
    SET IsActive = 1,
        UpdatedAt = SYSDATETIME()
    WHERE LTRIM(RTRIM(Name)) = @Name;
END
ELSE
BEGIN
    INSERT INTO {tableName} (Name, IsActive, CreatedAt)
    VALUES (@Name, 1, SYSDATETIME());
END;
", command =>
        {
            AddParameter(command, "@Name", ReferenceInput.Name);
        });

        TempData["SuccessMessage"] = "Reference option saved successfully.";
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        await SmartAttendance.Web.Infrastructure.Hrms.PositionSchema.EnsureAsync(_db);

                await EnsurePositionProfileColumnsAsync();
        await EnsurePositionReferenceTablesAsync();
await ExecuteNonQueryAsync(@"
UPDATE dbo.HrJobPositions
SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
    UpdatedAt = SYSDATETIME()
WHERE Id = @Id;
", command => AddParameter(command, "@Id", id));

        TempData["SuccessMessage"] = "تم تحديث حالة المنصب.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await SmartAttendance.Web.Infrastructure.Hrms.PositionSchema.EnsureAsync(_db);

                await EnsurePositionProfileColumnsAsync();
        await EnsurePositionReferenceTablesAsync();
var positionName = await GetPositionNameAsync(id);
        if (string.IsNullOrWhiteSpace(positionName))
        {
            TempData["ErrorMessage"] = "المنصب المطلوب حذفه غير موجود.";
            return RedirectToPage();
        }

        var linkedEmployees = await CountEmployeesForPositionAsync(positionName);
        if (linkedEmployees > 0)
        {
            TempData["ErrorMessage"] = "لا يمكن حذف منصب مرتبط بموظفين.";
            return RedirectToPage();
        }

        await ExecuteNonQueryAsync(@"
DELETE FROM dbo.HrJobPositions
WHERE Id = @Id;
", command => AddParameter(command, "@Id", id));

        TempData["SuccessMessage"] = "تم حذف المنصب بنجاح.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetEmployeesAsync(int id)
    {
        await SmartAttendance.Web.Infrastructure.Hrms.PositionSchema.EnsureAsync(_db);

                await EnsurePositionProfileColumnsAsync();
        await EnsurePositionReferenceTablesAsync();
var positionName = await GetPositionNameAsync(id);
        if (string.IsNullOrWhiteSpace(positionName))
        {
            return new JsonResult(new
            {
                ok = false,
                message = "المنصب غير موجود."
            });
        }

        var normalizedPositionName = NormalizeText(positionName);
        var employees = await _db.Employees
            .AsNoTracking()
            .Select(employee => new
            {
                employee.EmployeeNo,
                employee.FullName,
                employee.Position,
                employee.IsActive
            })
            .ToListAsync();

        var linkedEmployees = employees
            .Where(employee => string.Equals(NormalizeText(employee.Position), normalizedPositionName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(employee => employee.FullName)
            .Select(employee => new
            {
                name = employee.FullName,
                employeeNo = employee.EmployeeNo,
                status = employee.IsActive ? "فعال" : "غير فعال"
            })
            .ToList();

        return new JsonResult(new
        {
            ok = true,
            position = positionName,
            count = linkedEmployees.Count,
            employees = linkedEmployees
        });
    }

    private async Task LoadPageAsync(int? editId)
    {
        Departments = await _db.Departments
            .AsNoTracking()
            .Where(department => !department.IsDeleted)
            .OrderBy(department => department.Name)
            .Select(department => new DepartmentOption
            {
                Id = department.Id,
                Name = department.Name
            })
            .ToListAsync();

        Categories = await ReadNameOptionsAsync("dbo.HrJobPositionCategories");
        Levels = await ReadNameOptionsAsync("dbo.HrJobPositionLevels");
        CompetencyOptions = await ReadNameOptionsAsync("dbo.HrJobPositionCompetencyOptions");
        EducationOptions = await ReadNameOptionsAsync("dbo.HrJobPositionEducationOptions");
        EducationSpecializationOptions = await ReadNameOptionsAsync("dbo.HrJobPositionEducationSpecializationOptions");
        CertificationOptions = await ReadNameOptionsAsync("dbo.HrJobPositionCertificationOptions");

        var departmentNames = Departments.ToDictionary(department => department.Id, department => department.Name);
        var positionRows = await ReadPositionRowsAsync();

        var employees = await _db.Employees
            .AsNoTracking()
            .Select(employee => new
            {
                employee.Position,
                employee.IsActive
            })
            .ToListAsync();

        var employeeCounts = employees
            .Select(employee => new
            {
                Position = NormalizeText(employee.Position),
                employee.IsActive
            })
            .Where(employee => !string.IsNullOrWhiteSpace(employee.Position))
            .GroupBy(employee => employee.Position, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new EmployeeCountSummary
                {
                    Total = group.Count(),
                    Active = group.Count(employee => employee.IsActive),
                    Inactive = group.Count(employee => !employee.IsActive)
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var position in positionRows)
        {
            var normalizedName = NormalizeText(position.ArabicName);
            if (employeeCounts.TryGetValue(normalizedName, out var countSummary))
            {
                position.EmployeeCount = countSummary.Total;
                position.ActiveEmployeeCount = countSummary.Active;
                position.InactiveEmployeeCount = countSummary.Inactive;
            }

            if (position.DepartmentId.HasValue && departmentNames.TryGetValue(position.DepartmentId.Value, out var departmentName))
            {
                position.DepartmentName = departmentName;
            }
            else
            {
                position.DepartmentName = null;
            }

            position.SearchText = NormalizeSearchText(string.Join(" ", new[]
            {
                position.ArabicName,
                position.DepartmentName,
                position.Category,
                position.Level,
                position.Description,
                position.JobPurpose,
                position.KeyResponsibilities,
                position.JobRequirements,
                position.RequiredSkills,
                position.JobKpis,
                position.Competencies,
                position.Education,
                position.EducationSpecialization,
                position.Certifications
            }.Where(value => !string.IsNullOrWhiteSpace(value))));
        }

        Positions = positionRows
            .OrderByDescending(position => position.EmployeeCount)
            .ThenBy(position => position.ArabicName)
            .ToList();

        TotalPositions = Positions.Count;
        ActivePositions = Positions.Count(position => position.IsActive);
        LinkedPositions = Positions.Count(position => position.EmployeeCount > 0);
        TotalLinkedEmployees = Positions.Sum(position => position.EmployeeCount);

        if (editId.HasValue)
        {
            var editRow = Positions.FirstOrDefault(position => position.Id == editId.Value);
            if (editRow != null)
            {
                Input = new JobPositionForm
                {
                    Id = editRow.Id,
                    OriginalArabicName = editRow.ArabicName,
                    ArabicName = editRow.ArabicName,
                    DepartmentId = editRow.DepartmentId,
                    Category = editRow.Category,
                    Level = editRow.Level,
                    Description = editRow.Description,
                    JobPurpose = editRow.JobPurpose,
                    KeyResponsibilities = editRow.KeyResponsibilities,
                    JobRequirements = editRow.JobRequirements,
                    RequiredSkills = editRow.RequiredSkills,
                    JobKpis = editRow.JobKpis,
                    Competencies = editRow.Competencies,
                    Education = editRow.Education,
                    EducationSpecialization = editRow.EducationSpecialization,
                    Certifications = editRow.Certifications,
                    IsActive = editRow.IsActive
                };
            }
        }
    }

    private async Task SyncLookupTablesAsync()
    {
        await ExecuteNonQueryAsync(@"
INSERT INTO dbo.HrJobPositionCategories (Name, IsActive, CreatedAt)
SELECT v.Name, 1, SYSDATETIME()
FROM (VALUES
    (N'إداري'),
    (N'تشغيلي'),
    (N'فني'),
    (N'موقع'),
    (N'إشرافي'),
    (N'إدارة عليا')
) AS v(Name)
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.HrJobPositionCategories c
    WHERE LTRIM(RTRIM(c.Name)) = LTRIM(RTRIM(v.Name))
);

INSERT INTO dbo.HrJobPositionCategories (Name, IsActive, CreatedAt)
SELECT DISTINCT LTRIM(RTRIM(Category)), 1, SYSDATETIME()
FROM dbo.HrJobPositions p
WHERE p.Category IS NOT NULL
  AND LTRIM(RTRIM(p.Category)) <> N''
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.HrJobPositionCategories c
      WHERE LTRIM(RTRIM(c.Name)) = LTRIM(RTRIM(p.Category))
  );

INSERT INTO dbo.HrJobPositionLevels (Name, IsActive, CreatedAt)
SELECT v.Name, 1, SYSDATETIME()
FROM (VALUES
    (N'Junior'),
    (N'Mid'),
    (N'Senior'),
    (N'Supervisor'),
    (N'Manager'),
    (N'Director')
) AS v(Name)
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.HrJobPositionLevels l
    WHERE LTRIM(RTRIM(l.Name)) = LTRIM(RTRIM(v.Name))
);

INSERT INTO dbo.HrJobPositionLevels (Name, IsActive, CreatedAt)
SELECT DISTINCT LTRIM(RTRIM(Level)), 1, SYSDATETIME()
FROM dbo.HrJobPositions p
WHERE p.Level IS NOT NULL
  AND LTRIM(RTRIM(p.Level)) <> N''
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.HrJobPositionLevels l
      WHERE LTRIM(RTRIM(l.Name)) = LTRIM(RTRIM(p.Level))
  );
");
    }

    private async Task SyncEmployeePositionsAsync()
    {
        await ExecuteNonQueryAsync(@"
INSERT INTO dbo.HrJobPositions
(
    ArabicName,
    EnglishName,
    JobCode,
    DepartmentId,
    Grade,
    Category,
    Level,
    Description,
    IsActive,
    CreatedAt
)
SELECT DISTINCT
    LTRIM(RTRIM(CONVERT(NVARCHAR(400), employee.Position))) AS ArabicName,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    1,
    SYSDATETIME()
FROM dbo.Employees employee
WHERE employee.Position IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(NVARCHAR(400), employee.Position))) <> N''
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.HrJobPositions position
      WHERE LTRIM(RTRIM(position.ArabicName)) = LTRIM(RTRIM(CONVERT(NVARCHAR(400), employee.Position)))
  );
");
    }


    private async Task EnsurePositionProfileColumnsAsync()
    {
        await ExecuteNonQueryAsync(@"
IF COL_LENGTH('dbo.HrJobPositions', 'JobPurpose') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD JobPurpose NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.HrJobPositions', 'KeyResponsibilities') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD KeyResponsibilities NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.HrJobPositions', 'JobRequirements') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD JobRequirements NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.HrJobPositions', 'RequiredSkills') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD RequiredSkills NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.HrJobPositions', 'JobKpis') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD JobKpis NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.HrJobPositions', 'Competencies') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Competencies NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.HrJobPositions', 'Education') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Education NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.HrJobPositions', 'EducationSpecialization') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD EducationSpecialization NVARCHAR(MAX) NULL;

IF COL_LENGTH('dbo.HrJobPositions', 'Certifications') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Certifications NVARCHAR(MAX) NULL;
");
    }

    private async Task SavePositionProfileAsync(int id, string positionName)
    {
        await EnsurePositionProfileColumnsAsync();

                await EnsurePositionReferenceTablesAsync();
await ExecuteNonQueryAsync(@"
UPDATE dbo.HrJobPositions
SET JobPurpose = @JobPurpose,
    KeyResponsibilities = @KeyResponsibilities,
    JobRequirements = @JobRequirements,
    RequiredSkills = @RequiredSkills,
    JobKpis = @JobKpis,
    Competencies = @Competencies,
    Education = @Education,
    EducationSpecialization = @EducationSpecialization,
    Certifications = @Certifications,
    UpdatedAt = SYSDATETIME()
WHERE (@Id > 0 AND Id = @Id)
   OR (@Id <= 0 AND LTRIM(RTRIM(ArabicName)) = @ArabicName);
", command =>
        {
            AddParameter(command, "@Id", id);
            AddParameter(command, "@ArabicName", NormalizeText(positionName));
            AddParameter(command, "@JobPurpose", EmptyToNull(Input.JobPurpose));
            AddParameter(command, "@KeyResponsibilities", EmptyToNull(Input.KeyResponsibilities));
            AddParameter(command, "@JobRequirements", EmptyToNull(Input.JobRequirements));
            AddParameter(command, "@RequiredSkills", EmptyToNull(Input.RequiredSkills));
            AddParameter(command, "@JobKpis", EmptyToNull(Input.JobKpis));
            AddParameter(command, "@Competencies", EmptyToNull(Input.Competencies));
            AddParameter(command, "@Education", EmptyToNull(Input.Education));
            AddParameter(command, "@EducationSpecialization", EmptyToNull(Input.EducationSpecialization));
            AddParameter(command, "@Certifications", EmptyToNull(Input.Certifications));
        });
    }

    private async Task EnsurePositionReferenceTablesAsync()
    {
        await ExecuteNonQueryAsync(@"
IF OBJECT_ID('dbo.HrJobPositionCompetencyOptions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositionCompetencyOptions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositionCompetencyOptions PRIMARY KEY,
        Name NVARCHAR(240) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositionCompetencyOptions_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositionCompetencyOptions_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

IF OBJECT_ID('dbo.HrJobPositionEducationOptions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositionEducationOptions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositionEducationOptions PRIMARY KEY,
        Name NVARCHAR(240) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositionEducationOptions_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositionEducationOptions_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

IF OBJECT_ID('dbo.HrJobPositionEducationSpecializationOptions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositionEducationSpecializationOptions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositionEducationSpecializationOptions PRIMARY KEY,
        Name NVARCHAR(240) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositionEducationSpecializationOptions_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositionEducationSpecializationOptions_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

INSERT INTO dbo.HrJobPositionEducationSpecializationOptions (Name, IsActive, CreatedAt)
SELECT DISTINCT LTRIM(RTRIM(CONVERT(NVARCHAR(240), p.EducationSpecialization))), 1, SYSDATETIME()
FROM dbo.HrJobPositions p
WHERE COL_LENGTH('dbo.HrJobPositions', 'EducationSpecialization') IS NOT NULL
  AND p.EducationSpecialization IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), p.EducationSpecialization))) <> N''
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.HrJobPositionEducationSpecializationOptions x
      WHERE LTRIM(RTRIM(x.Name)) = LTRIM(RTRIM(CONVERT(NVARCHAR(240), p.EducationSpecialization)))
  );
IF OBJECT_ID('dbo.HrJobPositionCertificationOptions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositionCertificationOptions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositionCertificationOptions PRIMARY KEY,
        Name NVARCHAR(240) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositionCertificationOptions_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositionCertificationOptions_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

INSERT INTO dbo.HrJobPositionCompetencyOptions (Name, IsActive, CreatedAt)
SELECT DISTINCT LTRIM(RTRIM(CONVERT(NVARCHAR(240), p.Competencies))), 1, SYSDATETIME()
FROM dbo.HrJobPositions p
WHERE p.Competencies IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), p.Competencies))) <> N''
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.HrJobPositionCompetencyOptions x
      WHERE LTRIM(RTRIM(x.Name)) = LTRIM(RTRIM(CONVERT(NVARCHAR(240), p.Competencies)))
  );

INSERT INTO dbo.HrJobPositionEducationOptions (Name, IsActive, CreatedAt)
SELECT DISTINCT LTRIM(RTRIM(CONVERT(NVARCHAR(240), p.Education))), 1, SYSDATETIME()
FROM dbo.HrJobPositions p
WHERE p.Education IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), p.Education))) <> N''
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.HrJobPositionEducationOptions x
      WHERE LTRIM(RTRIM(x.Name)) = LTRIM(RTRIM(CONVERT(NVARCHAR(240), p.Education)))
  );

INSERT INTO dbo.HrJobPositionCertificationOptions (Name, IsActive, CreatedAt)
SELECT DISTINCT LTRIM(RTRIM(CONVERT(NVARCHAR(240), p.Certifications))), 1, SYSDATETIME()
FROM dbo.HrJobPositions p
WHERE p.Certifications IS NOT NULL
  AND LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), p.Certifications))) <> N''
  AND NOT EXISTS
  (
      SELECT 1
      FROM dbo.HrJobPositionCertificationOptions x
      WHERE LTRIM(RTRIM(x.Name)) = LTRIM(RTRIM(CONVERT(NVARCHAR(240), p.Certifications)))
  );
");
    }

    private static string? ResolvePositionReferenceTable(string? type)
    {
        return NormalizeText(type).ToLowerInvariant() switch
        {
            "competencies" => "dbo.HrJobPositionCompetencyOptions",
            "education" => "dbo.HrJobPositionEducationOptions",
            "specializations" => "dbo.HrJobPositionEducationSpecializationOptions",
            "certifications" => "dbo.HrJobPositionCertificationOptions",
            _ => null
        };
    }
    private async Task<List<JobPositionRow>> ReadPositionRowsAsync()
    {
        var rows = new List<JobPositionRow>();
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    Id,
    ArabicName,
    DepartmentId,
    Category,
    Level,
    Description,
    JobPurpose,
    KeyResponsibilities,
    JobRequirements,
    RequiredSkills,
    JobKpis,
    Competencies,
    Education,
    EducationSpecialization,
    Certifications,
    IsActive
FROM dbo.HrJobPositions
ORDER BY ArabicName;";

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new JobPositionRow
                {
                    Id = reader.GetInt32(0),
                    ArabicName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    DepartmentId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    Category = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Level = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                    JobPurpose = reader.IsDBNull(6) ? null : reader.GetString(6),
                    KeyResponsibilities = reader.IsDBNull(7) ? null : reader.GetString(7),
                    JobRequirements = reader.IsDBNull(8) ? null : reader.GetString(8),
                    RequiredSkills = reader.IsDBNull(9) ? null : reader.GetString(9),
                    JobKpis = reader.IsDBNull(10) ? null : reader.GetString(10),
                    Competencies = reader.IsDBNull(11) ? null : reader.GetString(11),
                    Education = reader.IsDBNull(12) ? null : reader.GetString(12),
                    EducationSpecialization = reader.IsDBNull(13) ? null : reader.GetString(13),
                    Certifications = reader.IsDBNull(14) ? null : reader.GetString(14),
                    IsActive = !reader.IsDBNull(15) && reader.GetBoolean(15)
                });
            }
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }

        return rows;
    }

    private async Task<List<NameOption>> ReadNameOptionsAsync(string tableName)
    {
        var options = new List<NameOption>();
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT Id, Name, IsActive FROM {tableName} ORDER BY Name;";

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                options.Add(new NameOption
                {
                    Id = reader.GetInt32(0),
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    IsActive = !reader.IsDBNull(2) && reader.GetBoolean(2)
                });
            }
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }

        return options;
    }

    private async Task<string?> GetPositionNameAsync(int id)
    {
        var value = await ExecuteScalarAsync(@"
SELECT TOP 1 ArabicName
FROM dbo.HrJobPositions
WHERE Id = @Id;
", command => AddParameter(command, "@Id", id));

        return value == null || value == DBNull.Value ? null : Convert.ToString(value);
    }

    private async Task<int> CountEmployeesForPositionAsync(string positionName)
    {
        var employees = await _db.Employees
            .AsNoTracking()
            .Select(employee => employee.Position)
            .ToListAsync();

        var normalizedName = NormalizeText(positionName);
        return employees.Count(position => string.Equals(NormalizeText(position), normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task UpdateEmployeesPositionNameAsync(string oldName, string newName)
    {
        await ExecuteNonQueryAsync(@"
UPDATE dbo.Employees
SET Position = @NewName,
    UpdatedAt = SYSDATETIME()
WHERE LTRIM(RTRIM(CONVERT(NVARCHAR(MAX), Position))) = @OldName;
", command =>
        {
            AddParameter(command, "@NewName", newName);
            AddParameter(command, "@OldName", NormalizeText(oldName));
        });
    }

    private async Task<object?> ExecuteScalarAsync(string sql, Action<DbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            return await command.ExecuteScalarAsync();
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task ExecuteNonQueryAsync(string sql, Action<DbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static object? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim(), @"\s+", " ");
    }

    private static string NormalizeSearchText(string? value)
    {
        return NormalizeText(value).ToLowerInvariant();
    }
}

public sealed class JobPositionForm
{
    public int Id { get; set; }

    public string? OriginalArabicName { get; set; }

    public string? ArabicName { get; set; }

    public int? DepartmentId { get; set; }

    public string? Category { get; set; }

    public string? Level { get; set; }

    public string? Description { get; set; }

    
    public string? JobPurpose { get; set; }

    public string? KeyResponsibilities { get; set; }

    public string? JobRequirements { get; set; }

    public string? RequiredSkills { get; set; }

    public string? JobKpis { get; set; }

    public string? Competencies { get; set; }

    public string? Education { get; set; }

    public string? EducationSpecialization { get; set; }

    public string? Certifications { get; set; }
public bool IsActive { get; set; } = true;
}

public sealed class JobPositionRow
{
    public int Id { get; set; }

    public string ArabicName { get; set; } = string.Empty;

    public int? DepartmentId { get; set; }

    public string? DepartmentName { get; set; }

    public string? Category { get; set; }

    public string? Level { get; set; }

    public string? Description { get; set; }

    
    public string? JobPurpose { get; set; }

    public string? KeyResponsibilities { get; set; }

    public string? JobRequirements { get; set; }

    public string? RequiredSkills { get; set; }

    public string? JobKpis { get; set; }

    public string? Competencies { get; set; }

    public string? Education { get; set; }

    public string? EducationSpecialization { get; set; }

    public string? Certifications { get; set; }

    public bool HasJobProfile =>
        !string.IsNullOrWhiteSpace(JobPurpose) ||
        !string.IsNullOrWhiteSpace(KeyResponsibilities) ||
        !string.IsNullOrWhiteSpace(JobRequirements) ||
        !string.IsNullOrWhiteSpace(RequiredSkills) ||
        !string.IsNullOrWhiteSpace(JobKpis) ||
        !string.IsNullOrWhiteSpace(Competencies) ||
        !string.IsNullOrWhiteSpace(Education) ||
        !string.IsNullOrWhiteSpace(EducationSpecialization) ||
        !string.IsNullOrWhiteSpace(Certifications);
public bool IsActive { get; set; }

    public int EmployeeCount { get; set; }

    public int ActiveEmployeeCount { get; set; }

    public int InactiveEmployeeCount { get; set; }

    public string SearchText { get; set; } = string.Empty;
}

public sealed class DepartmentOption
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

public sealed class NameOption
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

public sealed class EmployeeCountSummary
{
    public int Total { get; set; }

    public int Active { get; set; }

    public int Inactive { get; set; }
}


public sealed class PositionReferenceForm
{
    public string? Name { get; set; }
}