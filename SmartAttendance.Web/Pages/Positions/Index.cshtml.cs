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

    public List<JobPositionRow> Positions { get; set; } = new();

    public List<DepartmentOption> Departments { get; set; } = new();

    public List<NameOption> Categories { get; set; } = new();

    public List<NameOption> Levels { get; set; } = new();

    public int TotalPositions { get; set; }

    public int ActivePositions { get; set; }

    public int LinkedPositions { get; set; }

    public int TotalLinkedEmployees { get; set; }

    public async Task OnGetAsync(int? editId)
    {
        await EnsureSchemaAsync();
        await SyncLookupTablesAsync();
        await SyncEmployeePositionsAsync();
        await LoadPageAsync(editId);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await EnsureSchemaAsync();
        await SyncLookupTablesAsync();

        Input.ArabicName = NormalizeText(Input.ArabicName);
        Input.OriginalArabicName = NormalizeText(Input.OriginalArabicName);
        Input.Category = NormalizeText(Input.Category);
        Input.Level = NormalizeText(Input.Level);

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
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        await EnsureSchemaAsync();

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
        await EnsureSchemaAsync();

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
        await EnsureSchemaAsync();

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
                position.Description
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
                    IsActive = editRow.IsActive
                };
            }
        }
    }

    private async Task EnsureSchemaAsync()
    {
        await ExecuteNonQueryAsync(@"
IF OBJECT_ID(N'dbo.HrJobPositions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositions
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositions PRIMARY KEY,
        ArabicName NVARCHAR(400) NOT NULL,
        EnglishName NVARCHAR(400) NULL,
        JobCode NVARCHAR(160) NULL,
        DepartmentId INT NULL,
        Grade NVARCHAR(160) NULL,
        Category NVARCHAR(160) NULL,
        Level NVARCHAR(160) NULL,
        Description NVARCHAR(MAX) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositions_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositions_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

IF OBJECT_ID(N'dbo.HrJobPositionCategories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositionCategories
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositionCategories PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositionCategories_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositionCategories_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;

IF OBJECT_ID(N'dbo.HrJobPositionLevels', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.HrJobPositionLevels
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HrJobPositionLevels PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_HrJobPositionLevels_IsActive DEFAULT(1),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_HrJobPositionLevels_CreatedAt DEFAULT(SYSDATETIME()),
        UpdatedAt DATETIME2 NULL
    );
END;
");

        await ExecuteNonQueryAsync(@"
IF COL_LENGTH(N'dbo.HrJobPositions', N'ArabicName') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD ArabicName NVARCHAR(400) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'EnglishName') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD EnglishName NVARCHAR(400) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'JobCode') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD JobCode NVARCHAR(160) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'DepartmentId') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD DepartmentId INT NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'Grade') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Grade NVARCHAR(160) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'Category') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Category NVARCHAR(160) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'Level') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Level NVARCHAR(160) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'Description') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD Description NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'IsActive') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD IsActive BIT NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'CreatedAt') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD CreatedAt DATETIME2 NULL;
IF COL_LENGTH(N'dbo.HrJobPositions', N'UpdatedAt') IS NULL
    ALTER TABLE dbo.HrJobPositions ADD UpdatedAt DATETIME2 NULL;

UPDATE dbo.HrJobPositions SET IsActive = 1 WHERE IsActive IS NULL;
UPDATE dbo.HrJobPositions SET CreatedAt = SYSDATETIME() WHERE CreatedAt IS NULL;
");
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
                    IsActive = !reader.IsDBNull(6) && reader.GetBoolean(6)
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
