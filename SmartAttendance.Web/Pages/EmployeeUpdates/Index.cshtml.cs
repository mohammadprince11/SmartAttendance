using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeeUpdates;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Tab { get; private set; } = "stage";
    public string ActiveSectionKey { get; private set; } = "employee-info";
    public int SelectedEmployeeId { get; private set; }
    public UpdateEmployee SelectedEmployee { get; private set; } = UpdateEmployee.Empty;
    public List<UpdateEmployee> Employees { get; private set; } = new();
    public List<DepartmentOption> Departments { get; private set; } = new();
    public List<UpdateSection> Sections { get; private set; } = BuildSections();
    public UpdateSection ActiveSection => Sections.FirstOrDefault(x => x.Key == ActiveSectionKey) ?? Sections[0];
    public List<UpdateField> CurrentFields => ActiveSection.Fields;
    public Dictionary<string, string> CurrentValues { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<UpdateBatch> OpenBatches { get; private set; } = new();
    public List<UpdateBatch> HistoryBatches { get; private set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public string Initials => GetInitials(SelectedEmployee.FullName);

    public async Task<IActionResult> OnGetAsync(int? employeeId, string? tab, string? section)
    {
        await LoadPageAsync(employeeId, tab, section);
        return Page();
    }

    public async Task<IActionResult> OnPostStageAsync(int employeeId, string sectionKey, string? note)
    {
        await EnsureTablesAsync();

        var sections = BuildSections();
        var section = sections.FirstOrDefault(x => x.Key.Equals(sectionKey ?? string.Empty, StringComparison.OrdinalIgnoreCase)) ?? sections[0];
        var current = await BuildCurrentValuesAsync(employeeId);

        var changes = new List<UpdateChange>();
        foreach (var field in section.Fields)
        {
            var oldValue = NormalizeValue(current.GetValueOrDefault(field.Key, string.Empty));
            var newValue = NormalizeValue(Request.Form[field.Key].FirstOrDefault() ?? string.Empty);

            if (field.InputType == "select-active")
            {
                newValue = newValue.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
            }

            if (!oldValue.Equals(newValue, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new UpdateChange
                {
                    FieldKey = field.Key,
                    FieldLabel = field.Label,
                    OldValue = oldValue,
                    NewValue = newValue
                });
            }
        }

        if (changes.Count == 0)
        {
            StatusMessage = "لا توجد تغييرات لإنشاء حركة.";
            return RedirectToPage(new { employeeId, tab = "stage", section = section.Key });
        }

        var requestedBy = Request.Cookies["SA.UserName"] ?? User.Identity?.Name ?? "System";

        var batchId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
INSERT INTO EmployeeUpdateBatches
(EmployeeId, SectionKey, SectionName, Status, RequestedBy, RequestedAt, Note)
VALUES
(@EmployeeId, @SectionKey, @SectionName, 'Open', @RequestedBy, SYSUTCDATETIME(), @Note);

SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@SectionKey", section.Key);
                HrmsDatabase.AddParameter(command, "@SectionName", section.Name);
                HrmsDatabase.AddParameter(command, "@RequestedBy", requestedBy);
                HrmsDatabase.AddParameter(command, "@Note", note ?? string.Empty);
            });

        foreach (var change in changes)
        {
            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                """
INSERT INTO EmployeeUpdateChanges
(BatchId, FieldKey, FieldLabel, OldValue, NewValue)
VALUES
(@BatchId, @FieldKey, @FieldLabel, @OldValue, @NewValue);
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@BatchId", batchId);
                    HrmsDatabase.AddParameter(command, "@FieldKey", change.FieldKey);
                    HrmsDatabase.AddParameter(command, "@FieldLabel", change.FieldLabel);
                    HrmsDatabase.AddParameter(command, "@OldValue", change.OldValue);
                    HrmsDatabase.AddParameter(command, "@NewValue", change.NewValue);
                });
        }

        StatusMessage = $"تم إنشاء حركة غير مقفلة رقم {batchId}. راجعها في صفحة التأكيد قبل القفل.";
        return RedirectToPage(new { employeeId, tab = "confirm", section = section.Key });
    }

    public async Task<IActionResult> OnPostLockAsync(int batchId, int employeeId)
    {
        await EnsureTablesAsync();

        var batch = await LoadSingleBatchAsync(batchId);
        if (batch is null || !batch.Status.Equals("Open", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "الحركة غير موجودة أو تم قفلها مسبقاً.";
            return RedirectToPage(new { employeeId, tab = "confirm" });
        }

        var definitions = BuildFieldDictionary();
        foreach (var change in batch.Changes)
        {
            if (!definitions.TryGetValue(change.FieldKey, out var field))
            {
                await ApplyCustomFieldAsync(batch.EmployeeId, change.FieldKey, change.FieldLabel, change.NewValue);
                continue;
            }

            if (field.Target == "employee")
            {
                await ApplyEmployeeFieldAsync(batch.EmployeeId, change.FieldKey, change.NewValue);
            }
            else if (field.Target == "compensation")
            {
                await ApplyCompensationFieldAsync(batch.EmployeeId, change.FieldKey, change.NewValue);
            }
            else
            {
                await ApplyCustomFieldAsync(batch.EmployeeId, change.FieldKey, change.FieldLabel, change.NewValue);
            }
        }

        var lockedBy = Request.Cookies["SA.UserName"] ?? User.Identity?.Name ?? "System";

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
UPDATE EmployeeUpdateBatches
SET Status = 'Locked',
    LockedBy = @LockedBy,
    LockedAt = SYSUTCDATETIME()
WHERE Id = @BatchId AND Status = 'Open';
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@LockedBy", lockedBy);
                HrmsDatabase.AddParameter(command, "@BatchId", batchId);
            });

        StatusMessage = $"تم قفل الحركة رقم {batchId} وتطبيق التغييرات على ملف الموظف.";
        return RedirectToPage(new { employeeId, tab = "history" });
    }

    public async Task<IActionResult> OnPostDeleteOpenAsync(int batchId, int employeeId)
    {
        await EnsureTablesAsync();

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
DELETE FROM EmployeeUpdateChanges
WHERE BatchId IN (SELECT Id FROM EmployeeUpdateBatches WHERE Id = @BatchId AND Status = 'Open');

DELETE FROM EmployeeUpdateBatches
WHERE Id = @BatchId AND Status = 'Open';
""",
            command => HrmsDatabase.AddParameter(command, "@BatchId", batchId));

        StatusMessage = "تم حذف الحركة غير المقفلة.";
        return RedirectToPage(new { employeeId, tab = "confirm" });
    }

    private async Task LoadPageAsync(int? employeeId, string? tab, string? section)
    {
        await EnsureTablesAsync();

        Tab = NormalizeTab(tab);
        ActiveSectionKey = NormalizeSection(section);

        Employees = await LoadEmployeesAsync();
        Departments = await LoadDepartmentsAsync();

        SelectedEmployeeId = employeeId.GetValueOrDefault();
        if (SelectedEmployeeId <= 0)
        {
            SelectedEmployeeId = Employees.FirstOrDefault()?.Id ?? 0;
        }

        SelectedEmployee = await LoadEmployeeAsync(SelectedEmployeeId) ?? UpdateEmployee.Empty;
        CurrentValues = await BuildCurrentValuesAsync(SelectedEmployeeId);
        OpenBatches = await LoadBatchesAsync(SelectedEmployeeId, "Open");
        HistoryBatches = await LoadBatchesAsync(SelectedEmployeeId, "Locked");
    }

    private async Task EnsureTablesAsync()
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF OBJECT_ID('EmployeeUpdateBatches', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeUpdateBatches
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        SectionKey nvarchar(80) NOT NULL,
        SectionName nvarchar(150) NOT NULL,
        Status nvarchar(40) NOT NULL DEFAULT('Open'),
        RequestedBy nvarchar(150) NULL,
        RequestedAt datetime2 NOT NULL DEFAULT(SYSUTCDATETIME()),
        LockedBy nvarchar(150) NULL,
        LockedAt datetime2 NULL,
        Note nvarchar(max) NULL
    );
END;

IF OBJECT_ID('EmployeeUpdateChanges', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeUpdateChanges
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BatchId int NOT NULL,
        FieldKey nvarchar(100) NOT NULL,
        FieldLabel nvarchar(150) NOT NULL,
        OldValue nvarchar(max) NULL,
        NewValue nvarchar(max) NULL
    );
END;

IF OBJECT_ID('EmployeeCustomFields', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeCustomFields
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        FieldKey nvarchar(100) NOT NULL,
        FieldLabel nvarchar(150) NULL,
        FieldValue nvarchar(max) NULL,
        UpdatedAt datetime2 NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_EmployeeCustomFields_Employee_Field' AND object_id = OBJECT_ID('EmployeeCustomFields'))
BEGIN
    CREATE UNIQUE INDEX UX_EmployeeCustomFields_Employee_Field ON EmployeeCustomFields(EmployeeId, FieldKey);
END;

IF OBJECT_ID('EmployeeCompensations', 'U') IS NULL
BEGIN
    CREATE TABLE EmployeeCompensations
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EmployeeId int NOT NULL,
        BasicSalary decimal(18,2) NULL,
        Allowances decimal(18,2) NULL,
        Deductions decimal(18,2) NULL,
        PaymentMethod nvarchar(80) NULL,
        BankName nvarchar(150) NULL,
        BankAccount nvarchar(150) NULL,
        Currency nvarchar(30) NULL,
        UpdatedAt datetime2 NULL
    );
END;
""");
    }

    private async Task<List<UpdateEmployee>> LoadEmployeesAsync()
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 500
    e.Id,
    ISNULL(e.EmployeeNo, '') AS EmployeeNo,
    ISNULL(e.FullName, '') AS FullName,
    ISNULL(e.Position, '') AS Position,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
ORDER BY e.FullName;
""",
            null,
            reader => new UpdateEmployee
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName")
            });
    }

    private async Task<UpdateEmployee?> LoadEmployeeAsync(int employeeId)
    {
        var list = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    e.Id,
    ISNULL(e.EmployeeNo, '') AS EmployeeNo,
    ISNULL(e.FullName, '') AS FullName,
    ISNULL(e.Position, '') AS Position,
    ISNULL(d.Name, '') AS DepartmentName,
    ISNULL(b.Name, '') AS BranchName
FROM Employees e
LEFT JOIN Departments d ON e.DepartmentId = d.Id
LEFT JOIN Branches b ON d.BranchId = b.Id
WHERE e.Id = @EmployeeId;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader => new UpdateEmployee
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeNo = HrmsDatabase.GetString(reader, "EmployeeNo"),
                FullName = HrmsDatabase.GetString(reader, "FullName"),
                Position = HrmsDatabase.GetString(reader, "Position"),
                DepartmentName = HrmsDatabase.GetString(reader, "DepartmentName"),
                BranchName = HrmsDatabase.GetString(reader, "BranchName")
            });

        return list.FirstOrDefault();
    }

    private async Task<List<DepartmentOption>> LoadDepartmentsAsync()
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT d.Id, ISNULL(d.Name, '') AS Name, ISNULL(b.Name, '') AS BranchName
FROM Departments d
LEFT JOIN Branches b ON d.BranchId = b.Id
ORDER BY b.Name, d.Name;
""",
            null,
            reader => new DepartmentOption
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = $"{HrmsDatabase.GetString(reader, "Name")} - {HrmsDatabase.GetString(reader, "BranchName")}".Trim(' ', '-')
            });
    }

    private async Task<Dictionary<string, string>> BuildCurrentValuesAsync(int employeeId)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var employeeRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    ISNULL(EmployeeNo, '') AS EmployeeNo,
    ISNULL(FullName, '') AS FullName,
    ISNULL(NationalId, '') AS NationalId,
    ISNULL(Phone, '') AS Phone,
    ISNULL(Email, '') AS Email,
    ISNULL(Position, '') AS Position,
    HireDate,
    BirthDate,
    ISNULL(IsActive, 0) AS IsActive,
    ISNULL(DepartmentId, 0) AS DepartmentId
FROM Employees
WHERE Id = @EmployeeId;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader =>
            {
                values["EmployeeNo"] = HrmsDatabase.GetString(reader, "EmployeeNo");
                values["FullName"] = HrmsDatabase.GetString(reader, "FullName");
                values["NationalId"] = HrmsDatabase.GetString(reader, "NationalId");
                values["Phone"] = HrmsDatabase.GetString(reader, "Phone");
                values["Email"] = HrmsDatabase.GetString(reader, "Email");
                values["Position"] = HrmsDatabase.GetString(reader, "Position");
                values["HireDate"] = ToInputDate(HrmsDatabase.GetDateTime(reader, "HireDate"));
                values["BirthDate"] = ToInputDate(HrmsDatabase.GetDateTime(reader, "BirthDate"));
                values["IsActive"] = HrmsDatabase.GetBool(reader, "IsActive") ? "true" : "false";
                values["DepartmentId"] = HrmsDatabase.GetInt(reader, "DepartmentId").ToString();
                return true;
            });

        var compensationRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    ISNULL(BasicSalary, 0) AS BasicSalary,
    ISNULL(Allowances, 0) AS Allowances,
    ISNULL(Deductions, 0) AS Deductions,
    ISNULL(PaymentMethod, '') AS PaymentMethod,
    ISNULL(BankName, '') AS BankName,
    ISNULL(BankAccount, '') AS BankAccount,
    ISNULL(Currency, 'IQD') AS Currency
FROM EmployeeCompensations
WHERE EmployeeId = @EmployeeId
ORDER BY UpdatedAt DESC, Id DESC;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader =>
            {
                values["BasicSalary"] = ToDecimalString(reader, "BasicSalary");
                values["Allowances"] = ToDecimalString(reader, "Allowances");
                values["Deductions"] = ToDecimalString(reader, "Deductions");
                values["PaymentMethod"] = HrmsDatabase.GetString(reader, "PaymentMethod");
                values["BankName"] = HrmsDatabase.GetString(reader, "BankName");
                values["BankAccount"] = HrmsDatabase.GetString(reader, "BankAccount");
                values["Currency"] = HrmsDatabase.GetString(reader, "Currency");
                return true;
            });

        var customRows = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT FieldKey, ISNULL(FieldValue, '') AS FieldValue
FROM EmployeeCustomFields
WHERE EmployeeId = @EmployeeId;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId),
            reader =>
            {
                values[HrmsDatabase.GetString(reader, "FieldKey")] = HrmsDatabase.GetString(reader, "FieldValue");
                return true;
            });

        foreach (var field in BuildFieldDictionary().Values)
        {
            if (!values.ContainsKey(field.Key))
            {
                values[field.Key] = string.Empty;
            }
        }

        return values;
    }

    private async Task<List<UpdateBatch>> LoadBatchesAsync(int employeeId, string status)
    {
        var batches = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 50
    Id,
    EmployeeId,
    SectionKey,
    SectionName,
    Status,
    ISNULL(RequestedBy, '') AS RequestedBy,
    RequestedAt,
    ISNULL(LockedBy, '') AS LockedBy,
    LockedAt,
    ISNULL(Note, '') AS Note
FROM EmployeeUpdateBatches
WHERE EmployeeId = @EmployeeId AND Status = @Status
ORDER BY ISNULL(LockedAt, RequestedAt) DESC, Id DESC;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@Status", status);
            },
            reader => new UpdateBatch
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                SectionKey = HrmsDatabase.GetString(reader, "SectionKey"),
                SectionName = HrmsDatabase.GetString(reader, "SectionName"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                RequestedBy = HrmsDatabase.GetString(reader, "RequestedBy"),
                RequestedAt = HrmsDatabase.GetDateTime(reader, "RequestedAt"),
                LockedBy = HrmsDatabase.GetString(reader, "LockedBy"),
                LockedAt = HrmsDatabase.GetDateTime(reader, "LockedAt"),
                Note = HrmsDatabase.GetString(reader, "Note")
            });

        foreach (var batch in batches)
        {
            batch.Changes = await LoadChangesAsync(batch.Id);
        }

        return batches;
    }

    private async Task<UpdateBatch?> LoadSingleBatchAsync(int batchId)
    {
        var batches = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT TOP 1
    Id,
    EmployeeId,
    SectionKey,
    SectionName,
    Status,
    ISNULL(RequestedBy, '') AS RequestedBy,
    RequestedAt,
    ISNULL(LockedBy, '') AS LockedBy,
    LockedAt,
    ISNULL(Note, '') AS Note
FROM EmployeeUpdateBatches
WHERE Id = @BatchId;
""",
            command => HrmsDatabase.AddParameter(command, "@BatchId", batchId),
            reader => new UpdateBatch
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                EmployeeId = HrmsDatabase.GetInt(reader, "EmployeeId"),
                SectionKey = HrmsDatabase.GetString(reader, "SectionKey"),
                SectionName = HrmsDatabase.GetString(reader, "SectionName"),
                Status = HrmsDatabase.GetString(reader, "Status"),
                RequestedBy = HrmsDatabase.GetString(reader, "RequestedBy"),
                RequestedAt = HrmsDatabase.GetDateTime(reader, "RequestedAt"),
                LockedBy = HrmsDatabase.GetString(reader, "LockedBy"),
                LockedAt = HrmsDatabase.GetDateTime(reader, "LockedAt"),
                Note = HrmsDatabase.GetString(reader, "Note")
            });

        var batch = batches.FirstOrDefault();
        if (batch is not null)
        {
            batch.Changes = await LoadChangesAsync(batch.Id);
        }

        return batch;
    }

    private async Task<List<UpdateChange>> LoadChangesAsync(int batchId)
    {
        return await HrmsDatabase.QueryAsync(
            _dbContext,
            """
SELECT Id, BatchId, FieldKey, FieldLabel, ISNULL(OldValue, '') AS OldValue, ISNULL(NewValue, '') AS NewValue
FROM EmployeeUpdateChanges
WHERE BatchId = @BatchId
ORDER BY Id;
""",
            command => HrmsDatabase.AddParameter(command, "@BatchId", batchId),
            reader => new UpdateChange
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                BatchId = HrmsDatabase.GetInt(reader, "BatchId"),
                FieldKey = HrmsDatabase.GetString(reader, "FieldKey"),
                FieldLabel = HrmsDatabase.GetString(reader, "FieldLabel"),
                OldValue = HrmsDatabase.GetString(reader, "OldValue"),
                NewValue = HrmsDatabase.GetString(reader, "NewValue")
            });
    }

    private async Task ApplyEmployeeFieldAsync(int employeeId, string fieldKey, string value)
    {
        switch (fieldKey)
        {
            case "FullName":
                await UpdateEmployeeStringAsync(employeeId, "FullName", value);
                break;
            case "EmployeeNo":
                await UpdateEmployeeStringAsync(employeeId, "EmployeeNo", value);
                break;
            case "NationalId":
                await UpdateEmployeeStringAsync(employeeId, "NationalId", value);
                break;
            case "Phone":
                await UpdateEmployeeStringAsync(employeeId, "Phone", value);
                break;
            case "Email":
                await UpdateEmployeeStringAsync(employeeId, "Email", value);
                break;
            case "Position":
                await UpdateEmployeeStringAsync(employeeId, "Position", value);
                break;
            case "DepartmentId":
                await HrmsDatabase.ExecuteAsync(
                    _dbContext,
                    "UPDATE Employees SET DepartmentId = @Value WHERE Id = @EmployeeId;",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@Value", int.TryParse(value, out var id) ? id : 0);
                        HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    });
                break;
            case "HireDate":
                await UpdateEmployeeDateAsync(employeeId, "HireDate", value);
                break;
            case "BirthDate":
                await UpdateEmployeeDateAsync(employeeId, "BirthDate", value);
                break;
            case "IsActive":
                await HrmsDatabase.ExecuteAsync(
                    _dbContext,
                    "UPDATE Employees SET IsActive = @Value WHERE Id = @EmployeeId;",
                    command =>
                    {
                        HrmsDatabase.AddParameter(command, "@Value", value.Equals("true", StringComparison.OrdinalIgnoreCase));
                        HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                    });
                break;
        }
    }

    private async Task UpdateEmployeeStringAsync(int employeeId, string column, string value)
    {
        var sql = column switch
        {
            "FullName" => "UPDATE Employees SET FullName = @Value WHERE Id = @EmployeeId;",
            "EmployeeNo" => "UPDATE Employees SET EmployeeNo = @Value WHERE Id = @EmployeeId;",
            "NationalId" => "UPDATE Employees SET NationalId = @Value WHERE Id = @EmployeeId;",
            "Phone" => "UPDATE Employees SET Phone = @Value WHERE Id = @EmployeeId;",
            "Email" => "UPDATE Employees SET Email = @Value WHERE Id = @EmployeeId;",
            "Position" => "UPDATE Employees SET Position = @Value WHERE Id = @EmployeeId;",
            _ => throw new InvalidOperationException("Unsupported employee field.")
        };

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            sql,
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Value", value ?? string.Empty);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            });
    }

    private async Task UpdateEmployeeDateAsync(int employeeId, string column, string value)
    {
        var sql = column switch
        {
            "HireDate" => "UPDATE Employees SET HireDate = @Value WHERE Id = @EmployeeId;",
            "BirthDate" => "UPDATE Employees SET BirthDate = @Value WHERE Id = @EmployeeId;",
            _ => throw new InvalidOperationException("Unsupported employee date field.")
        };

        DateTime? parsed = DateTime.TryParse(value, out var date) ? date : null;

        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            sql,
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Value", parsed);
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
            });
    }

    private async Task ApplyCompensationFieldAsync(int employeeId, string fieldKey, string value)
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF NOT EXISTS (SELECT 1 FROM EmployeeCompensations WHERE EmployeeId = @EmployeeId)
BEGIN
    INSERT INTO EmployeeCompensations (EmployeeId, Currency, UpdatedAt)
    VALUES (@EmployeeId, 'IQD', SYSUTCDATETIME());
END;
""",
            command => HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId));

        if (fieldKey is "BasicSalary" or "Allowances" or "Deductions")
        {
            var number = decimal.TryParse(value, out var d) ? d : 0;
            var sql = fieldKey switch
            {
                "BasicSalary" => "UPDATE EmployeeCompensations SET BasicSalary = @Value, UpdatedAt = SYSUTCDATETIME() WHERE EmployeeId = @EmployeeId;",
                "Allowances" => "UPDATE EmployeeCompensations SET Allowances = @Value, UpdatedAt = SYSUTCDATETIME() WHERE EmployeeId = @EmployeeId;",
                "Deductions" => "UPDATE EmployeeCompensations SET Deductions = @Value, UpdatedAt = SYSUTCDATETIME() WHERE EmployeeId = @EmployeeId;",
                _ => throw new InvalidOperationException("Unsupported compensation numeric field.")
            };

            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                sql,
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Value", number);
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                });
        }
        else
        {
            var sql = fieldKey switch
            {
                "PaymentMethod" => "UPDATE EmployeeCompensations SET PaymentMethod = @Value, UpdatedAt = SYSUTCDATETIME() WHERE EmployeeId = @EmployeeId;",
                "BankName" => "UPDATE EmployeeCompensations SET BankName = @Value, UpdatedAt = SYSUTCDATETIME() WHERE EmployeeId = @EmployeeId;",
                "BankAccount" => "UPDATE EmployeeCompensations SET BankAccount = @Value, UpdatedAt = SYSUTCDATETIME() WHERE EmployeeId = @EmployeeId;",
                "Currency" => "UPDATE EmployeeCompensations SET Currency = @Value, UpdatedAt = SYSUTCDATETIME() WHERE EmployeeId = @EmployeeId;",
                _ => throw new InvalidOperationException("Unsupported compensation text field.")
            };

            await HrmsDatabase.ExecuteAsync(
                _dbContext,
                sql,
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Value", value ?? string.Empty);
                    HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                });
        }
    }

    private async Task ApplyCustomFieldAsync(int employeeId, string fieldKey, string fieldLabel, string value)
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF EXISTS (SELECT 1 FROM EmployeeCustomFields WHERE EmployeeId = @EmployeeId AND FieldKey = @FieldKey)
BEGIN
    UPDATE EmployeeCustomFields
    SET FieldLabel = @FieldLabel,
        FieldValue = @FieldValue,
        UpdatedAt = SYSUTCDATETIME()
    WHERE EmployeeId = @EmployeeId AND FieldKey = @FieldKey;
END
ELSE
BEGIN
    INSERT INTO EmployeeCustomFields (EmployeeId, FieldKey, FieldLabel, FieldValue, UpdatedAt)
    VALUES (@EmployeeId, @FieldKey, @FieldLabel, @FieldValue, SYSUTCDATETIME());
END;
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@FieldKey", fieldKey);
                HrmsDatabase.AddParameter(command, "@FieldLabel", fieldLabel);
                HrmsDatabase.AddParameter(command, "@FieldValue", value ?? string.Empty);
            });
    }

    public string FieldValue(string key) => CurrentValues.TryGetValue(key, out var value) ? value : string.Empty;

    public string FieldDisplayValue(string key, string? value)
    {
        value ??= string.Empty;
        if (key.Equals("IsActive", StringComparison.OrdinalIgnoreCase))
        {
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "فعال" : "غير فعال";
        }

        if (key.Equals("DepartmentId", StringComparison.OrdinalIgnoreCase))
        {
            return Departments.FirstOrDefault(x => x.Id.ToString() == value)?.Name ?? "-";
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        if (key is "BasicSalary" or "Allowances" or "Deductions" && decimal.TryParse(value, out var d))
        {
            return d.ToString("N0");
        }

        return value;
    }

    public string DisplayValue(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
    public string DisplayDateTime(DateTime? value) => value.HasValue ? value.Value.ToString("dd/MM/yyyy HH:mm") : "-";

    private static string ToInputDate(DateTime? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd") : string.Empty;

    private static string ToDecimalString(System.Data.Common.DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal)) return string.Empty;
        var value = Convert.ToDecimal(reader.GetValue(ordinal));
        return value == 0 ? string.Empty : value.ToString("0.##");
    }

    private static string NormalizeValue(string? value) => (value ?? string.Empty).Trim();

    private string NormalizeTab(string? tab)
    {
        return tab?.ToLowerInvariant() switch
        {
            "confirm" => "confirm",
            "history" => "history",
            _ => "stage"
        };
    }

    private string NormalizeSection(string? section)
    {
        if (string.Equals(section, "basic", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(section, "recruitment", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(section, "contact", StringComparison.OrdinalIgnoreCase))
        {
            return "employee-info";
        }

        if (!string.IsNullOrWhiteSpace(section) && Sections.Any(x => x.Key.Equals(section, StringComparison.OrdinalIgnoreCase)))
        {
            return section;
        }

        return Sections[0].Key;
    }

    private string GetInitials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "م";
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][0].ToString();
        return $"{parts[0][0]}{parts[^1][0]}";
    }

    private static List<UpdateSection> BuildSections()
    {
        return new List<UpdateSection>
        {
            new("employee-info", "معلومات الموظف", "البيانات الأساسية والتوظيف والاتصال في حركة واحدة: الاسم، المنصب، القسم، المدير، الهاتف، البريد",
                new()
                {
                    new("FullName", "اسم الموظف", "employee", "text", "مثال: محمد علي"),
                    new("EmployeeNo", "الرقم الوظيفي", "employee", "text", "مثال: EMP-001"),
                    new("NationalId", "رقم الهوية / البطاقة", "employee", "text", "مثال: 123456"),
                    new("BirthDate", "تاريخ الميلاد", "employee", "date", ""),
                    new("Position", "المنصب", "employee", "text", "مثال: HR Officer"),
                    new("DepartmentId", "القسم / الفرع", "employee", "select-department", ""),
                    new("ManagerName", "المدير المباشر", "custom", "text", "مثال: اسم المدير المباشر"),
                    new("HireDate", "تاريخ المباشرة", "employee", "date", ""),
                    new("Phone", "رقم الهاتف", "employee", "text", "مثال: 0770xxxxxxx"),
                    new("Email", "البريد الإلكتروني", "employee", "text", "name@company.com"),
                    new("IsActive", "حالة الموظف", "employee", "select-active", "")
                }),

            new("attendance", "معلومات الحضور", "الشفت، نظام الدوام، السماحية، ساعات العمل",
                new()
                {
                    new("ShiftName", "الشفت", "custom", "text", "مثال: Morning Shift"),
                    new("AttendanceRule", "نظام الدوام", "custom", "text", "مثال: HQ 7h / Site 9h"),
                    new("GraceMinutes", "سماحية التأخير بالدقائق", "custom", "number", "مثال: 10"),
                    new("WorkHours", "ساعات العمل اليومية", "custom", "number", "مثال: 9")
                }),

            new("financial", "المعلومات المالية", "الراتب الأساسي والبدلات والاستقطاعات",
                new()
                {
                    new("BasicSalary", "الراتب الأساسي", "compensation", "number", "مثال: 750000"),
                    new("Allowances", "البدلات", "compensation", "number", "مثال: 50000"),
                    new("Deductions", "الاستقطاعات الثابتة", "compensation", "number", "مثال: 0")
                }),

            new("payment", "معلومات الدفع", "طريقة الدفع، البنك، الحساب، العملة",
                new()
                {
                    new("PaymentMethod", "طريقة الدفع", "compensation", "text", "Cash / Bank / Card"),
                    new("BankName", "اسم البنك", "compensation", "text", "مثال: مصرف الرافدين"),
                    new("BankAccount", "رقم الحساب", "compensation", "text", "مثال: IQ..."),
                    new("Currency", "العملة", "compensation", "text", "IQD / USD")
                }),

            new("extra", "حقول إضافية", "أي معلومات إضافية تحتاجها الشركة",
                new()
                {
                    new("ContractType", "نوع العقد", "custom", "text", "دوام كامل / جزئي / مؤقت"),
                    new("Nationality", "الجنسية", "custom", "text", "مثال: عراقي"),
                    new("Accommodation", "السكن", "custom", "text", "داخلي / خارجي"),
                    new("EmergencyContact", "جهة اتصال للطوارئ", "custom", "text", "الاسم والرقم")
                })
        };
    }

    private Dictionary<string, UpdateField> BuildFieldDictionary()
    {
        return BuildSections()
            .SelectMany(x => x.Fields)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }

    public record UpdateSection(string Key, string Name, string Description, List<UpdateField> Fields);
    public record UpdateField(string Key, string Label, string Target, string InputType, string Placeholder);

    public class UpdateEmployee
    {
        public static UpdateEmployee Empty => new() { FullName = "لا يوجد موظف", EmployeeNo = "-", Position = "-", DepartmentName = "-", BranchName = "-" };
        public int Id { get; set; }
        public string EmployeeNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
    }

    public class DepartmentOption
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class UpdateBatch
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public string SectionKey { get; set; } = string.Empty;
        public string SectionName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string RequestedBy { get; set; } = string.Empty;
        public DateTime? RequestedAt { get; set; }
        public string LockedBy { get; set; } = string.Empty;
        public DateTime? LockedAt { get; set; }
        public string Note { get; set; } = string.Empty;
        public List<UpdateChange> Changes { get; set; } = new();
    }

    public class UpdateChange
    {
        public int Id { get; set; }
        public int BatchId { get; set; }
        public string FieldKey { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
    }
}
