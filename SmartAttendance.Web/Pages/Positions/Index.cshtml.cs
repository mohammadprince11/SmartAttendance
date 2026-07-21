using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Pages.Positions;

/// <summary>
/// المناصب (الهيكل الوظيفي): جدول المناصب بفئاتها ومستوياتها مع بحث وترقيم —
/// يغذي قوائم المنصب بشاشات الموظف والهيكل التنظيمي.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    [BindProperty]
    public JobPositionForm Input { get; set; } = new();

    [BindProperty]
    public PositionReferenceForm ReferenceInput { get; set; } = new();

    public List<CompanyOption> Companies { get; set; } = new();

    public List<JobPositionRow> Positions { get; set; } = new();

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

    public string SelectedCompanyName { get; set; } = string.Empty;

    public async Task OnGetAsync(int? editId)
    {
        await LoadPageAsync(editId);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        Input.ArabicName = NormalizeText(Input.ArabicName);
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

        CompanyId = Input.CompanyId > 0
            ? Input.CompanyId
            : CompanyId;

        var selectedCompanyId = CompanyId.GetValueOrDefault();

        if (selectedCompanyId <= 0 ||
            !await CompanyExistsAsync(selectedCompanyId))
        {
            TempData["ErrorMessage"] = "\u064a\u062c\u0628 \u0627\u062e\u062a\u064a\u0627\u0631 \u0634\u0631\u0643\u0629 \u0635\u062d\u064a\u062d\u0629.";
            await LoadPageAsync(Input.Id > 0 ? Input.Id : null);
            return Page();
        }

        Input.CompanyId = selectedCompanyId;

        if (string.IsNullOrWhiteSpace(Input.ArabicName))
        {
            TempData["ErrorMessage"] = "\u0627\u0633\u0645 \u0627\u0644\u0645\u0646\u0635\u0628 \u0645\u0637\u0644\u0648\u0628.";
            await LoadPageAsync(Input.Id > 0 ? Input.Id : null);
            return Page();
        }

        var duplicateCount = Convert.ToInt32(await ExecuteScalarAsync(
            """
            SELECT COUNT(1)
            FROM dbo.HrJobPositions
            WHERE CompanyId = @CompanyId
              AND LTRIM(RTRIM(ArabicName)) = @ArabicName
              AND Id <> @Id;
            """,
            command =>
            {
                AddParameter(command, "@CompanyId", selectedCompanyId);
                AddParameter(command, "@ArabicName", Input.ArabicName);
                AddParameter(command, "@Id", Input.Id);
            }) ?? 0);

        if (duplicateCount > 0)
        {
            TempData["ErrorMessage"] = "\u0627\u0633\u0645 \u0627\u0644\u0645\u0646\u0635\u0628 \u0645\u0648\u062c\u0648\u062f \u0645\u0633\u0628\u0642\u0627\u064b \u062f\u0627\u062e\u0644 \u0627\u0644\u0634\u0631\u0643\u0629 \u0627\u0644\u0645\u062d\u062f\u062f\u0629.";
            await LoadPageAsync(Input.Id > 0 ? Input.Id : null);
            return Page();
        }

        if (Input.Id > 0)
        {
            var existing = await GetPositionIdentityAsync(Input.Id);

            if (existing == null ||
                existing.CompanyId != selectedCompanyId)
            {
                TempData["ErrorMessage"] = "\u0627\u0644\u0645\u0646\u0635\u0628 \u0627\u0644\u0645\u0637\u0644\u0648\u0628 \u062a\u0639\u062f\u064a\u0644\u0647 \u063a\u064a\u0631 \u0645\u0648\u062c\u0648\u062f \u062f\u0627\u062e\u0644 \u0627\u0644\u0634\u0631\u0643\u0629 \u0627\u0644\u0645\u062d\u062f\u062f\u0629.";
                return RedirectToPage(new { companyId = selectedCompanyId });
            }

            await ExecuteNonQueryAsync(
                """
                UPDATE dbo.HrJobPositions
                SET ArabicName = @ArabicName,
                    DepartmentId = NULL,
                    Category = @Category,
                    Level = @Level,
                    Description = @Description,
                    JobPurpose = @JobPurpose,
                    KeyResponsibilities = @KeyResponsibilities,
                    JobRequirements = @JobRequirements,
                    RequiredSkills = @RequiredSkills,
                    JobKpis = @JobKpis,
                    Competencies = @Competencies,
                    Education = @Education,
                    EducationSpecialization = @EducationSpecialization,
                    Certifications = @Certifications,
                    IsActive = @IsActive,
                    UpdatedAt = SYSDATETIME()
                WHERE Id = @Id
                  AND CompanyId = @CompanyId;
                """,
                command =>
                {
                    AddPositionParameters(command, Input);
                    AddParameter(command, "@Id", Input.Id);
                    AddParameter(command, "@CompanyId", selectedCompanyId);
                });

            if (!string.Equals(
                    NormalizeText(existing.Name),
                    Input.ArabicName,
                    StringComparison.OrdinalIgnoreCase))
            {
                await UpdateEmployeesPositionNameAsync(
                    Input.Id,
                    Input.ArabicName);
            }

            TempData["SuccessMessage"] = "\u062a\u0645 \u062a\u0639\u062f\u064a\u0644 \u0627\u0644\u0645\u0646\u0635\u0628 \u0628\u0646\u062c\u0627\u062d.";
        }
        else
        {
            await ExecuteNonQueryAsync(
                """
                INSERT INTO dbo.HrJobPositions
                (
                    CompanyId,
                    ArabicName,
                    EnglishName,
                    JobCode,
                    DepartmentId,
                    Grade,
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
                    IsActive,
                    CreatedAt
                )
                VALUES
                (
                    @CompanyId,
                    @ArabicName,
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    @Category,
                    @Level,
                    @Description,
                    @JobPurpose,
                    @KeyResponsibilities,
                    @JobRequirements,
                    @RequiredSkills,
                    @JobKpis,
                    @Competencies,
                    @Education,
                    @EducationSpecialization,
                    @Certifications,
                    1,
                    SYSDATETIME()
                );
                """,
                command =>
                {
                    AddParameter(command, "@CompanyId", selectedCompanyId);
                    AddPositionParameters(command, Input);
                });

            TempData["SuccessMessage"] = "\u062a\u0645 \u062d\u0641\u0638 \u0627\u0644\u0645\u0646\u0635\u0628 \u0628\u0646\u062c\u0627\u062d.";
        }

        return RedirectToPage(new { companyId = selectedCompanyId });
    }

    public async Task<IActionResult> OnPostSaveReferenceAsync(string type)
    {
        var tableName = ResolvePositionReferenceTable(type);
        ReferenceInput.Name = NormalizeText(ReferenceInput.Name);

        if (string.IsNullOrWhiteSpace(tableName))
        {
            TempData["ErrorMessage"] = "\u0646\u0648\u0639 \u0627\u0644\u0642\u0627\u0626\u0645\u0629 \u0627\u0644\u0645\u0631\u062c\u0639\u064a\u0629 \u063a\u064a\u0631 \u0635\u062d\u064a\u062d.";
            return RedirectToPage(new { companyId = CompanyId });
        }

        if (string.IsNullOrWhiteSpace(ReferenceInput.Name))
        {
            TempData["ErrorMessage"] = "\u0627\u0633\u0645 \u0627\u0644\u062e\u064a\u0627\u0631 \u0627\u0644\u0645\u0631\u062c\u0639\u064a \u0645\u0637\u0644\u0648\u0628.";
            return RedirectToPage(new { companyId = CompanyId });
        }

        await ExecuteNonQueryAsync(
            $"""
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
            """,
            command => AddParameter(
                command,
                "@Name",
                ReferenceInput.Name));

        TempData["SuccessMessage"] = "\u062a\u0645 \u062d\u0641\u0638 \u0627\u0644\u062e\u064a\u0627\u0631 \u0627\u0644\u0645\u0631\u062c\u0639\u064a \u0628\u0646\u062c\u0627\u062d.";
        return RedirectToPage(new { companyId = CompanyId });
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var identity = await GetPositionIdentityAsync(id);

        if (identity == null)
        {
            TempData["ErrorMessage"] = "\u0627\u0644\u0645\u0646\u0635\u0628 \u063a\u064a\u0631 \u0645\u0648\u062c\u0648\u062f.";
            return RedirectToPage(new { companyId = CompanyId });
        }

        await ExecuteNonQueryAsync(
            """
            UPDATE dbo.HrJobPositions
            SET IsActive = CASE WHEN IsActive = 1 THEN 0 ELSE 1 END,
                UpdatedAt = SYSDATETIME()
            WHERE Id = @Id;
            """,
            command => AddParameter(command, "@Id", id));

        TempData["SuccessMessage"] = "\u062a\u0645 \u062a\u062d\u062f\u064a\u062b \u062d\u0627\u0644\u0629 \u0627\u0644\u0645\u0646\u0635\u0628.";
        return RedirectToPage(new { companyId = identity.CompanyId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var identity = await GetPositionIdentityAsync(id);

        if (identity == null)
        {
            TempData["ErrorMessage"] = "\u0627\u0644\u0645\u0646\u0635\u0628 \u0627\u0644\u0645\u0637\u0644\u0648\u0628 \u062d\u0630\u0641\u0647 \u063a\u064a\u0631 \u0645\u0648\u062c\u0648\u062f.";
            return RedirectToPage(new { companyId = CompanyId });
        }

        var linkedEmployees = await _db.Employees
            .AsNoTracking()
            .CountAsync(employee => employee.PositionId == id);

        if (linkedEmployees > 0)
        {
            TempData["ErrorMessage"] = "\u0644\u0627 \u064a\u0645\u0643\u0646 \u062d\u0630\u0641 \u0645\u0646\u0635\u0628 \u0645\u0631\u062a\u0628\u0637 \u0628\u0645\u0648\u0638\u0641\u064a\u0646.";
            return RedirectToPage(new { companyId = identity.CompanyId });
        }

        await ExecuteNonQueryAsync(
            """
            DELETE FROM dbo.HrJobPositions
            WHERE Id = @Id;
            """,
            command => AddParameter(command, "@Id", id));

        TempData["SuccessMessage"] = "\u062a\u0645 \u062d\u0630\u0641 \u0627\u0644\u0645\u0646\u0635\u0628 \u0628\u0646\u062c\u0627\u062d.";
        return RedirectToPage(new { companyId = identity.CompanyId });
    }

    public async Task<IActionResult> OnGetEmployeesAsync(int id)
    {
        var identity = await GetPositionIdentityAsync(id);

        if (identity == null)
        {
            return new JsonResult(new
            {
                ok = false,
                message = "\u0627\u0644\u0645\u0646\u0635\u0628 \u063a\u064a\u0631 \u0645\u0648\u062c\u0648\u062f."
            });
        }

        var employees = await _db.Employees
            .AsNoTracking()
            .Where(employee => employee.PositionId == id)
            .OrderBy(employee => employee.FullName)
            .Select(employee => new
            {
                name = employee.FullName,
                employeeNo = employee.EmployeeNo,
                status = employee.IsActive
                    ? "\u0641\u0639\u0627\u0644"
                    : "\u063a\u064a\u0631 \u0641\u0639\u0627\u0644"
            })
            .ToListAsync();

        return new JsonResult(new
        {
            ok = true,
            position = identity.Name,
            count = employees.Count,
            employees
        });
    }

    private async Task LoadPageAsync(int? editId)
    {
        Companies = await _db.Companies
            .AsNoTracking()
            .Where(company => !company.IsDeleted)
            .OrderBy(company => company.Name)
            .ThenBy(company => company.Code)
            .Select(company => new CompanyOption
            {
                Id = company.Id,
                Code = company.Code,
                Name = company.Name,
                IsActive = company.IsActive
            })
            .ToListAsync();

        if (Companies.Count == 0)
        {
            CompanyId = null;
            Input = new JobPositionForm();
            return;
        }

        var selectedCompanyId =
            CompanyId.HasValue &&
            Companies.Any(company => company.Id == CompanyId.Value)
                ? CompanyId.Value
                : Companies[0].Id;

        CompanyId = selectedCompanyId;
        SelectedCompanyName = Companies
            .First(company => company.Id == selectedCompanyId)
            .Name;

        Categories = await ReadNameOptionsAsync(
            "dbo.HrJobPositionCategories");
        Levels = await ReadNameOptionsAsync(
            "dbo.HrJobPositionLevels");
        CompetencyOptions = await ReadNameOptionsAsync(
            "dbo.HrJobPositionCompetencyOptions");
        EducationOptions = await ReadNameOptionsAsync(
            "dbo.HrJobPositionEducationOptions");
        EducationSpecializationOptions = await ReadNameOptionsAsync(
            "dbo.HrJobPositionEducationSpecializationOptions");
        CertificationOptions = await ReadNameOptionsAsync(
            "dbo.HrJobPositionCertificationOptions");

        var positionRows = await ReadPositionRowsAsync(selectedCompanyId);

        var employeeCounts = await _db.Employees
            .AsNoTracking()
            .Where(employee => employee.PositionId.HasValue)
            .GroupBy(employee => employee.PositionId!.Value)
            .Select(group => new
            {
                PositionId = group.Key,
                Total = group.Count(),
                Active = group.Count(employee => employee.IsActive),
                Inactive = group.Count(employee => !employee.IsActive)
            })
            .ToDictionaryAsync(item => item.PositionId);

        foreach (var position in positionRows)
        {
            position.CompanyName = SelectedCompanyName;

            if (employeeCounts.TryGetValue(
                    position.Id,
                    out var countSummary))
            {
                position.EmployeeCount = countSummary.Total;
                position.ActiveEmployeeCount = countSummary.Active;
                position.InactiveEmployeeCount = countSummary.Inactive;
            }

            position.SearchText = NormalizeSearchText(string.Join(
                " ",
                new[]
                {
                    position.ArabicName,
                    position.CompanyName,
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
        LinkedPositions = Positions.Count(
            position => position.EmployeeCount > 0);
        TotalLinkedEmployees = Positions.Sum(
            position => position.EmployeeCount);

        Input = new JobPositionForm
        {
            CompanyId = selectedCompanyId,
            IsActive = true
        };

        if (editId.HasValue)
        {
            var editRow = Positions.FirstOrDefault(
                position => position.Id == editId.Value);

            if (editRow != null)
            {
                Input = new JobPositionForm
                {
                    Id = editRow.Id,
                    CompanyId = editRow.CompanyId,
                    OriginalArabicName = editRow.ArabicName,
                    ArabicName = editRow.ArabicName,
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
                    EducationSpecialization =
                        editRow.EducationSpecialization,
                    Certifications = editRow.Certifications,
                    IsActive = editRow.IsActive
                };
            }
        }
    }

    private async Task<bool> CompanyExistsAsync(int companyId)
    {
        return await _db.Companies
            .AsNoTracking()
            .AnyAsync(company =>
                company.Id == companyId &&
                !company.IsDeleted &&
                company.IsActive);
    }

    private async Task<PositionIdentity?> GetPositionIdentityAsync(int id)
    {
        var result = new List<PositionIdentity>();
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT TOP 1 Id, CompanyId, ArabicName
                FROM dbo.HrJobPositions
                WHERE Id = @Id;
                """;
            AddParameter(command, "@Id", id);

            await using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new PositionIdentity
                {
                    Id = reader.GetInt32(0),
                    CompanyId = reader.GetInt32(1),
                    Name = reader.IsDBNull(2)
                        ? string.Empty
                        : reader.GetString(2)
                };
            }

            return null;
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<List<JobPositionRow>> ReadPositionRowsAsync(
        int companyId)
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

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    Id,
                    CompanyId,
                    ArabicName,
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
                WHERE CompanyId = @CompanyId
                ORDER BY ArabicName;
                """;
            AddParameter(command, "@CompanyId", companyId);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                rows.Add(new JobPositionRow
                {
                    Id = reader.GetInt32(0),
                    CompanyId = reader.GetInt32(1),
                    ArabicName = GetNullableString(reader, 2) ??
                                 string.Empty,
                    Category = GetNullableString(reader, 3),
                    Level = GetNullableString(reader, 4),
                    Description = GetNullableString(reader, 5),
                    JobPurpose = GetNullableString(reader, 6),
                    KeyResponsibilities = GetNullableString(reader, 7),
                    JobRequirements = GetNullableString(reader, 8),
                    RequiredSkills = GetNullableString(reader, 9),
                    JobKpis = GetNullableString(reader, 10),
                    Competencies = GetNullableString(reader, 11),
                    Education = GetNullableString(reader, 12),
                    EducationSpecialization =
                        GetNullableString(reader, 13),
                    Certifications = GetNullableString(reader, 14),
                    IsActive = !reader.IsDBNull(15) &&
                               reader.GetBoolean(15)
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

    private async Task<List<NameOption>> ReadNameOptionsAsync(
        string tableName)
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

            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT Id, Name, IsActive FROM {tableName} ORDER BY Name;";

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                options.Add(new NameOption
                {
                    Id = reader.GetInt32(0),
                    Name = reader.IsDBNull(1)
                        ? string.Empty
                        : reader.GetString(1),
                    IsActive = !reader.IsDBNull(2) &&
                               reader.GetBoolean(2)
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

    private async Task UpdateEmployeesPositionNameAsync(
        int positionId,
        string newName)
    {
        await ExecuteNonQueryAsync(
            """
            UPDATE dbo.Employees
            SET Position = @NewName,
                UpdatedAt = SYSDATETIME()
            WHERE PositionId = @PositionId;
            """,
            command =>
            {
                AddParameter(command, "@NewName", newName);
                AddParameter(command, "@PositionId", positionId);
            });
    }

    private static string? ResolvePositionReferenceTable(string? type)
    {
        return NormalizeText(type).ToLowerInvariant() switch
        {
            "competencies" =>
                "dbo.HrJobPositionCompetencyOptions",
            "education" =>
                "dbo.HrJobPositionEducationOptions",
            "specializations" =>
                "dbo.HrJobPositionEducationSpecializationOptions",
            "certifications" =>
                "dbo.HrJobPositionCertificationOptions",
            _ => null
        };
    }

    private static void AddPositionParameters(
        DbCommand command,
        JobPositionForm input)
    {
        AddParameter(command, "@ArabicName", input.ArabicName);
        AddParameter(command, "@Category", EmptyToNull(input.Category));
        AddParameter(command, "@Level", EmptyToNull(input.Level));
        AddParameter(
            command,
            "@Description",
            EmptyToNull(input.Description));
        AddParameter(
            command,
            "@JobPurpose",
            EmptyToNull(input.JobPurpose));
        AddParameter(
            command,
            "@KeyResponsibilities",
            EmptyToNull(input.KeyResponsibilities));
        AddParameter(
            command,
            "@JobRequirements",
            EmptyToNull(input.JobRequirements));
        AddParameter(
            command,
            "@RequiredSkills",
            EmptyToNull(input.RequiredSkills));
        AddParameter(
            command,
            "@JobKpis",
            EmptyToNull(input.JobKpis));
        AddParameter(
            command,
            "@Competencies",
            EmptyToNull(input.Competencies));
        AddParameter(
            command,
            "@Education",
            EmptyToNull(input.Education));
        AddParameter(
            command,
            "@EducationSpecialization",
            EmptyToNull(input.EducationSpecialization));
        AddParameter(
            command,
            "@Certifications",
            EmptyToNull(input.Certifications));
        AddParameter(command, "@IsActive", input.IsActive);
    }

    private async Task<object?> ExecuteScalarAsync(
        string sql,
        Action<DbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
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

    private async Task ExecuteNonQueryAsync(
        string sql,
        Action<DbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
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

    private static void AddParameter(
        DbCommand command,
        string name,
        object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string? GetNullableString(
        DbDataReader reader,
        int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);
    }

    private static object? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
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

    public int CompanyId { get; set; }

    public string? OriginalArabicName { get; set; }

    public string? ArabicName { get; set; }

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

    public int CompanyId { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string ArabicName { get; set; } = string.Empty;

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

public sealed class CompanyOption
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

public sealed class NameOption
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

public sealed class PositionReferenceForm
{
    public string? Name { get; set; }
}

internal sealed class PositionIdentity
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    public string Name { get; set; } = string.Empty;
}
