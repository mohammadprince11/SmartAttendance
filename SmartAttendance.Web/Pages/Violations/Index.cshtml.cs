using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Pages.Violations;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db)
    {
        _db = db;
    }

    public List<ViolationCaseRow> Items { get; private set; } = new();

    public List<EmployeePickerItem> EmployeeOptions { get; private set; } = new();

    public List<ViolationCategoryPickerItem> CategoryOptions { get; private set; } = new();

    public List<ViolationTypePickerItem> ViolationOptions { get; private set; } = new();

    public int TotalCount => Items.Count;

    public bool OpenCreateModal { get; private set; }

    [BindProperty]
    public CreateViolationInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        Input.EventDate = DateTime.Today;
        await LoadPageDataAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        await EnsureViolationCaseTableAsync();

        if (Input.EmployeeId <= 0)
        {
            ModelState.AddModelError("Input.EmployeeId", "اختر الموظف بالكود أو الاسم.");
        }

        if (Input.CategoryId <= 0)
        {
            ModelState.AddModelError("Input.CategoryId", "اختر فئة المخالفة أولاً.");
        }

        if (Input.ViolationTypeId <= 0)
        {
            ModelState.AddModelError("Input.ViolationTypeId", "اختر نوع المخالفة.");
        }

        if (Input.EventDate == default)
        {
            ModelState.AddModelError("Input.EventDate", "حدد تاريخ الحدث.");
        }

        var employeeExists = await ScalarAsync<int>(
            "SELECT COUNT(1) FROM Employees WHERE Id = @Id AND ISNULL(IsDeleted, 0) = 0;",
            command => Add(command, "@Id", Input.EmployeeId));

        if (employeeExists == 0 && Input.EmployeeId > 0)
        {
            ModelState.AddModelError("Input.EmployeeId", "الموظف المحدد غير موجود.");
        }

        var selectedRule = Input.ViolationTypeId > 0
            ? await GetViolationRuleAsync(Input.ViolationTypeId)
            : null;

        if (selectedRule == null && Input.ViolationTypeId > 0)
        {
            ModelState.AddModelError("Input.ViolationTypeId", "نوع المخالفة المحدد غير موجود.");
        }

        if (selectedRule != null && Input.CategoryId > 0 && selectedRule.CategoryId != Input.CategoryId)
        {
            ModelState.AddModelError("Input.ViolationTypeId", "نوع المخالفة لا يتبع الفئة المختارة.");
        }

        if (!ModelState.IsValid)
        {
            OpenCreateModal = true;
            await LoadPageDataAsync();
            return Page();
        }

        var penaltyAction = string.IsNullOrWhiteSpace(Input.FinalPenaltyAction)
            ? selectedRule!.PenaltyAction
            : Input.FinalPenaltyAction.Trim();

        var referenceNo = await GenerateReferenceNoAsync();

        await ExecuteAsync(
            """
INSERT INTO EmployeeViolationCases
(ReferenceNo, EmployeeId, ViolationTypeId, PenaltyRuleId, ViolationCategory, ViolationTitle,
 EventDate, Source, ActionStatus, Status, ProposedAction, FinalPenaltyAction,
 FinancialImpactType, FinancialImpactValue, DeductionAmount, Notes, CreatedAt, IsDeleted)
VALUES
(@ReferenceNo, @EmployeeId, @ViolationTypeId, @PenaltyRuleId, @ViolationCategory, @ViolationTitle,
 @EventDate, @Source, @ActionStatus, @Status, @ProposedAction, @FinalPenaltyAction,
 @FinancialImpactType, @FinancialImpactValue, @DeductionAmount, @Notes, SYSUTCDATETIME(), 0);
""",
            command =>
            {
                Add(command, "@ReferenceNo", referenceNo);
                Add(command, "@EmployeeId", Input.EmployeeId);
                Add(command, "@ViolationTypeId", Input.ViolationTypeId);
                Add(command, "@PenaltyRuleId", selectedRule!.PenaltyRuleId > 0 ? selectedRule.PenaltyRuleId : DBNull.Value);
                Add(command, "@ViolationCategory", selectedRule.CategoryName);
                Add(command, "@ViolationTitle", selectedRule.ViolationName);
                Add(command, "@EventDate", Input.EventDate.Date);
                Add(command, "@Source", string.IsNullOrWhiteSpace(Input.Source) ? "مباشر" : Input.Source.Trim());
                Add(command, "@ActionStatus", string.IsNullOrWhiteSpace(Input.ActionStatus) ? "بانتظار الإجراء" : Input.ActionStatus.Trim());
                Add(command, "@Status", string.IsNullOrWhiteSpace(Input.Status) ? "مسودة" : Input.Status.Trim());
                Add(command, "@ProposedAction", string.IsNullOrWhiteSpace(Input.ProposedAction) ? DBNull.Value : Input.ProposedAction.Trim());
                Add(command, "@FinalPenaltyAction", string.IsNullOrWhiteSpace(penaltyAction) ? DBNull.Value : penaltyAction);
                Add(command, "@FinancialImpactType", string.IsNullOrWhiteSpace(Input.FinancialImpactType) ? selectedRule.FinancialImpactType : Input.FinancialImpactType.Trim());
                Add(command, "@FinancialImpactValue", Input.FinancialImpactValue < 0 ? 0 : Input.FinancialImpactValue);
                Add(command, "@DeductionAmount", Input.DeductionAmount < 0 ? 0 : Input.DeductionAmount);
                Add(command, "@Notes", string.IsNullOrWhiteSpace(Input.Notes) ? DBNull.Value : Input.Notes.Trim());
            });

        TempData["SuccessMessage"] = $"تم تسجيل المخالفة بنجاح بالرقم المرجعي {referenceNo}.";
        return RedirectToPage();
    }

    private async Task LoadPageDataAsync()
    {
        await EnsureViolationCaseTableAsync();

        EmployeeOptions = await QueryAsync(
            """
SELECT Id, ISNULL(EmployeeNo, N'') AS EmployeeNo, ISNULL(FullName, N'') AS FullName
FROM Employees
WHERE ISNULL(IsDeleted, 0) = 0 AND ISNULL(IsActive, 1) = 1
ORDER BY FullName;
""",
            reader => new EmployeePickerItem
            {
                Id = ToInt(reader["Id"]),
                EmployeeNo = ToStringValue(reader["EmployeeNo"]),
                FullName = ToStringValue(reader["FullName"])
            });

        CategoryOptions = await LoadCategoryOptionsAsync();
        ViolationOptions = await LoadViolationOptionsAsync();
        Items = await LoadViolationRowsAsync();
    }

    private async Task<List<ViolationCategoryPickerItem>> LoadCategoryOptionsAsync()
    {
        var exists = await ScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID('DisciplinaryViolationCategories', 'U') IS NOT NULL THEN 1 ELSE 0 END;");

        if (exists == 0)
        {
            return new List<ViolationCategoryPickerItem>();
        }

        return await QueryAsync(
            """
SELECT Id, Name
FROM DisciplinaryViolationCategories
WHERE ISNULL(IsActive, 1) = 1
ORDER BY ISNULL(DisplayOrder, 999), Name;
""",
            reader => new ViolationCategoryPickerItem
            {
                Id = ToInt(reader["Id"]),
                Name = ToStringValue(reader["Name"])
            });
    }

    private async Task<List<ViolationTypePickerItem>> LoadViolationOptionsAsync()
    {
        var exists = await ScalarAsync<int>(
            """
SELECT CASE WHEN OBJECT_ID('DisciplinaryViolationTypes', 'U') IS NOT NULL
          AND OBJECT_ID('DisciplinaryViolationCategories', 'U') IS NOT NULL
          THEN 1 ELSE 0 END;
""");

        if (exists == 0)
        {
            return new List<ViolationTypePickerItem>();
        }

        var hasPenaltyTable = await ScalarAsync<int>(
            "SELECT CASE WHEN OBJECT_ID('DisciplinaryPenaltyRules', 'U') IS NOT NULL THEN 1 ELSE 0 END;");

        var sql = hasPenaltyTable == 1
            ? """
SELECT
    t.Id,
    t.CategoryId,
    ISNULL(c.Name, N'') AS CategoryName,
    t.Name AS ViolationName,
    ISNULL(p.Id, 0) AS PenaltyRuleId,
    ISNULL(p.PenaltyAction, N'') AS PenaltyAction,
    ISNULL(p.FinancialImpactType, N'None') AS FinancialImpactType,
    ISNULL(p.FinancialValue, 0) AS FinancialValue
FROM DisciplinaryViolationTypes t
LEFT JOIN DisciplinaryViolationCategories c ON c.Id = t.CategoryId
OUTER APPLY
(
    SELECT TOP 1 r.Id, r.PenaltyAction, r.FinancialImpactType, r.FinancialValue
    FROM DisciplinaryPenaltyRules r
    WHERE r.ViolationTypeId = t.Id
      AND ISNULL(r.IsActive, 1) = 1
    ORDER BY r.OccurrenceFrom, r.Id
) p
WHERE ISNULL(t.IsActive, 1) = 1
ORDER BY ISNULL(c.DisplayOrder, 999), c.Name, t.Name;
"""
            : """
SELECT
    t.Id,
    t.CategoryId,
    ISNULL(c.Name, N'') AS CategoryName,
    t.Name AS ViolationName,
    0 AS PenaltyRuleId,
    N'' AS PenaltyAction,
    N'None' AS FinancialImpactType,
    CAST(0 AS decimal(18,2)) AS FinancialValue
FROM DisciplinaryViolationTypes t
LEFT JOIN DisciplinaryViolationCategories c ON c.Id = t.CategoryId
WHERE ISNULL(t.IsActive, 1) = 1
ORDER BY ISNULL(c.DisplayOrder, 999), c.Name, t.Name;
""";

        return await QueryAsync(sql, reader => new ViolationTypePickerItem
        {
            Id = ToInt(reader["Id"]),
            CategoryId = ToInt(reader["CategoryId"]),
            CategoryName = ToStringValue(reader["CategoryName"]),
            ViolationName = ToStringValue(reader["ViolationName"]),
            PenaltyRuleId = ToInt(reader["PenaltyRuleId"]),
            PenaltyAction = ToStringValue(reader["PenaltyAction"]),
            FinancialImpactType = ToStringValue(reader["FinancialImpactType"], "None"),
            FinancialValue = ToDecimal(reader["FinancialValue"])
        });
    }

    private async Task<List<ViolationCaseRow>> LoadViolationRowsAsync()
    {
        return await QueryAsync(
            """
SELECT
    v.Id,
    v.ReferenceNo,
    v.EmployeeId,
    ISNULL(e.EmployeeNo, N'') AS EmployeeCode,
    ISNULL(e.FullName, N'') AS EmployeeName,
    ISNULL(e.Position, N'-') AS Position,
    ISNULL(d.Name, N'-') AS DepartmentName,
    ISNULL(b.Name, N'-') AS BranchName,
    ISNULL(v.ViolationCategory, N'') AS ViolationCategory,
    ISNULL(v.ViolationTitle, N'') AS ViolationTitle,
    v.EventDate,
    ISNULL(v.Source, N'') AS Source,
    ISNULL(v.ActionStatus, N'') AS ActionStatus,
    ISNULL(v.Status, N'') AS Status,
    ISNULL(v.FinalPenaltyAction, ISNULL(v.ProposedAction, N'')) AS FinalPenaltyAction,
    ISNULL(v.FinancialImpactType, N'None') AS FinancialImpactType,
    ISNULL(v.FinancialImpactValue, 0) AS FinancialImpactValue,
    ISNULL(v.DeductionAmount, 0) AS DeductionAmount,
    ISNULL(v.Notes, N'') AS Notes
FROM EmployeeViolationCases v
LEFT JOIN Employees e ON e.Id = v.EmployeeId
LEFT JOIN Departments d ON d.Id = e.DepartmentId
LEFT JOIN Branches b ON b.Id = d.BranchId
WHERE ISNULL(v.IsDeleted, 0) = 0
ORDER BY v.EventDate DESC, v.Id DESC;
""",
            reader => new ViolationCaseRow
            {
                Id = ToInt(reader["Id"]),
                ReferenceNo = ToStringValue(reader["ReferenceNo"]),
                EmployeeId = ToInt(reader["EmployeeId"]),
                EmployeeCode = ToStringValue(reader["EmployeeCode"]),
                EmployeeName = ToStringValue(reader["EmployeeName"]),
                Position = ToStringValue(reader["Position"], "-"),
                Branch = ToStringValue(reader["BranchName"], "-"),
                Department = ToStringValue(reader["DepartmentName"], "-"),
                ViolationCategory = ToStringValue(reader["ViolationCategory"]),
                ViolationTitle = ToStringValue(reader["ViolationTitle"]),
                EventDate = ToDate(reader["EventDate"]),
                Source = ToStringValue(reader["Source"]),
                ActionStatus = ToStringValue(reader["ActionStatus"]),
                Status = ToStringValue(reader["Status"]),
                FinalPenaltyAction = ToStringValue(reader["FinalPenaltyAction"]),
                FinancialImpactType = ToStringValue(reader["FinancialImpactType"], "None"),
                FinancialImpactValue = ToDecimal(reader["FinancialImpactValue"]),
                DeductionAmount = ToDecimal(reader["DeductionAmount"]),
                Notes = ToStringValue(reader["Notes"])
            });
    }

    private async Task<ViolationTypePickerItem?> GetViolationRuleAsync(int violationTypeId)
    {
        return (await LoadViolationOptionsAsync()).FirstOrDefault(x => x.Id == violationTypeId);
    }

    private async Task<string> GenerateReferenceNoAsync()
    {
        var prefix = $"VC{DateTime.Today:yy}-";
        var count = await ScalarAsync<int>(
            "SELECT COUNT(1) FROM EmployeeViolationCases WHERE ReferenceNo LIKE @Prefix;",
            command => Add(command, "@Prefix", prefix + "%"));

        return $"{prefix}{count + 1:000}";
    }

    private async Task EnsureViolationCaseTableAsync()
    {
        await ExecuteAsync(
            """
IF OBJECT_ID('EmployeeViolationCases', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeViolationCases
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ReferenceNo nvarchar(50) NOT NULL,
        EmployeeId int NOT NULL,
        ViolationTypeId int NULL,
        PenaltyRuleId int NULL,
        ViolationCategory nvarchar(150) NOT NULL DEFAULT(N''),
        ViolationTitle nvarchar(500) NOT NULL DEFAULT(N''),
        EventDate date NOT NULL,
        Source nvarchar(50) NOT NULL DEFAULT(N'مباشر'),
        ActionStatus nvarchar(100) NOT NULL DEFAULT(N'بانتظار الإجراء'),
        Status nvarchar(100) NOT NULL DEFAULT(N'مسودة'),
        ProposedAction nvarchar(300) NULL,
        FinalPenaltyAction nvarchar(300) NULL,
        FinancialImpactType nvarchar(40) NOT NULL DEFAULT(N'None'),
        FinancialImpactValue decimal(18,2) NOT NULL DEFAULT(0),
        DeductionAmount decimal(18,2) NOT NULL DEFAULT(0),
        Notes nvarchar(1000) NULL,
        FinalAction nvarchar(300) NULL,
        ApprovedAt datetime2 NULL,
        ClosedAt datetime2 NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt datetime2 NULL,
        IsDeleted bit NOT NULL DEFAULT(0)
    );
END;

IF COL_LENGTH('EmployeeViolationCases', 'ViolationTypeId') IS NULL
    ALTER TABLE EmployeeViolationCases ADD ViolationTypeId int NULL;

IF COL_LENGTH('EmployeeViolationCases', 'PenaltyRuleId') IS NULL
    ALTER TABLE EmployeeViolationCases ADD PenaltyRuleId int NULL;

IF COL_LENGTH('EmployeeViolationCases', 'FinalPenaltyAction') IS NULL
    ALTER TABLE EmployeeViolationCases ADD FinalPenaltyAction nvarchar(300) NULL;

IF COL_LENGTH('EmployeeViolationCases', 'FinancialImpactType') IS NULL
    ALTER TABLE EmployeeViolationCases ADD FinancialImpactType nvarchar(40) NOT NULL CONSTRAINT DF_EmployeeViolationCases_FinancialImpactType DEFAULT(N'None');

IF COL_LENGTH('EmployeeViolationCases', 'FinancialImpactValue') IS NULL
    ALTER TABLE EmployeeViolationCases ADD FinancialImpactValue decimal(18,2) NOT NULL CONSTRAINT DF_EmployeeViolationCases_FinancialImpactValue DEFAULT(0);

IF COL_LENGTH('EmployeeViolationCases', 'DeductionAmount') IS NULL
    ALTER TABLE EmployeeViolationCases ADD DeductionAmount decimal(18,2) NOT NULL CONSTRAINT DF_EmployeeViolationCases_DeductionAmount DEFAULT(0);

IF COL_LENGTH('EmployeeViolationCases', 'IsDeleted') IS NULL
    ALTER TABLE EmployeeViolationCases ADD IsDeleted bit NOT NULL CONSTRAINT DF_EmployeeViolationCases_IsDeleted DEFAULT(0);

IF COL_LENGTH('EmployeeViolationCases', 'UpdatedAt') IS NULL
    ALTER TABLE EmployeeViolationCases ADD UpdatedAt datetime2 NULL;
""");
    }

    private async Task ExecuteAsync(string sql, Action<IDbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<T> ScalarAsync<T>(string sql, Action<IDbCommand>? configure = null)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);
            var value = await command.ExecuteScalarAsync();

            if (value == null || value == DBNull.Value)
            {
                return default!;
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<List<T>> QueryAsync<T>(string sql, Func<IDataRecord, T> map, Action<IDbCommand>? configure = null)
    {
        var result = new List<T>();
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            configure?.Invoke(command);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(map(reader));
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }

    private static void Add(IDbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static int ToInt(object value) => value == DBNull.Value ? 0 : Convert.ToInt32(value);

    private static decimal ToDecimal(object value) => value == DBNull.Value ? 0 : Convert.ToDecimal(value);

    private static DateTime ToDate(object value) => value == DBNull.Value ? DateTime.Today : Convert.ToDateTime(value);

    private static string ToStringValue(object value, string fallback = "") => value == DBNull.Value ? fallback : Convert.ToString(value) ?? fallback;
}

public sealed class CreateViolationInput
{
    [Required(ErrorMessage = "اختر الموظف بالكود أو الاسم.")]
    [Display(Name = "الموظف")]
    public int EmployeeId { get; set; }

    [Required(ErrorMessage = "اختر فئة المخالفة.")]
    [Display(Name = "الفئة")]
    public int CategoryId { get; set; }

    [Required(ErrorMessage = "اختر نوع المخالفة.")]
    [Display(Name = "نوع المخالفة")]
    public int ViolationTypeId { get; set; }

    [Required(ErrorMessage = "حدد تاريخ الحدث.")]
    [DataType(DataType.Date)]
    [Display(Name = "تاريخ الحدث")]
    public DateTime EventDate { get; set; } = DateTime.Today;

    [Required]
    [StringLength(50)]
    [Display(Name = "المصدر")]
    public string Source { get; set; } = "مباشر";

    [Required]
    [StringLength(100)]
    [Display(Name = "حالة الإجراء")]
    public string ActionStatus { get; set; } = "بانتظار الإجراء";

    [Required]
    [StringLength(100)]
    [Display(Name = "الحالة")]
    public string Status { get; set; } = "مسودة";

    [StringLength(300)]
    [Display(Name = "العقوبة")]
    public string? FinalPenaltyAction { get; set; }

    [StringLength(40)]
    [Display(Name = "نوع أثر اللائحة")]
    public string FinancialImpactType { get; set; } = "None";

    [Range(0, 999999999)]
    [Display(Name = "قيمة أثر اللائحة")]
    public decimal FinancialImpactValue { get; set; }

    [Range(0, 999999999)]
    [Display(Name = "مبلغ الخصم بالدينار")]
    public decimal DeductionAmount { get; set; }

    [StringLength(300)]
    [Display(Name = "الإجراء المقترح")]
    public string? ProposedAction { get; set; }

    [StringLength(1000)]
    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }
}

public sealed class EmployeePickerItem
{
    public int Id { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    public string SearchText => $"{EmployeeNo} - {FullName}";
}

public sealed class ViolationCategoryPickerItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class ViolationTypePickerItem
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string ViolationName { get; set; } = string.Empty;
    public int PenaltyRuleId { get; set; }
    public string PenaltyAction { get; set; } = string.Empty;
    public string FinancialImpactType { get; set; } = "None";
    public decimal FinancialValue { get; set; }
}

public sealed class ViolationCaseRow
{
    public int Id { get; set; }
    public string ReferenceNo { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string ViolationCategory { get; set; } = string.Empty;
    public string ViolationTitle { get; set; } = string.Empty;
    public DateTime EventDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ActionStatus { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string FinalPenaltyAction { get; set; } = string.Empty;
    public string FinancialImpactType { get; set; } = "None";
    public decimal FinancialImpactValue { get; set; }
    public decimal DeductionAmount { get; set; }
    public string Notes { get; set; } = string.Empty;

    public string Avatar => string.IsNullOrWhiteSpace(EmployeeName) ? "؟" : EmployeeName.Trim()[0].ToString();

    public string StatusCss => Status switch
    {
        "موافق عليه" => "ok",
        "تم اتخاذ الإجراء" => "done",
        "بانتظار الاعتماد" => "pending",
        "مرفوض" => "rejected",
        _ => "draft"
    };

    public string DisplayFinancialImpact => FinancialImpactType switch
    {
        "Days" => $"{FinancialImpactValue:0.##} يوم حسب اللائحة",
        "Hours" => $"{FinancialImpactValue:0.##} ساعة حسب اللائحة",
        "Amount" => $"{FinancialImpactValue:0.##} مبلغ ثابت حسب اللائحة",
        _ => "لا يوجد"
    };
}
