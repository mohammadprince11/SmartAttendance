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
    public List<UpdateSection> StageBlocks => BuildStageBlocks(Sections);
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

    // NEXORA_FIX14B_STAGE_METHOD_START
    public async Task<IActionResult> OnPostStageAsync(int employeeId, string sectionKey, DateTime? effectiveDate, string? note)
    {
        await EmployeeUpdateSchema.EnsureAsync(_dbContext);
        await EnsureMovementColumnsAsync();

        var sections = await BuildSectionsWithDynamicFieldsAsync();
        var stageBlocks = BuildStageBlocks(sections);
        var stagedFields = stageBlocks
            .SelectMany(block => block.Fields)
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var current = await BuildCurrentValuesAsync(employeeId);

        var changes = new List<UpdateChange>();
        foreach (var field in stagedFields)
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
            StatusMessage = "\u0644\u0627 \u062A\u0648\u062C\u062F \u062A\u063A\u064A\u064A\u0631\u0627\u062A \u0644\u0625\u0646\u0634\u0627\u0621 \u062D\u0631\u0643\u0629.";
            return RedirectToPage(new { employeeId, tab = "stage", section = "employee-master" });
        }

        var requestedBy = Request.Cookies["SA.UserName"] ?? User.Identity?.Name ?? "System";
        var resolvedEffectiveDate = (effectiveDate ?? DateTime.Today).Date;
        var sectionName = "\u062A\u062D\u062F\u064A\u062B \u0628\u064A\u0627\u0646\u0627\u062A \u0627\u0644\u0645\u0648\u0638\u0641";

        var batchId = await HrmsDatabase.ScalarAsync<int>(
            _dbContext,
            """
INSERT INTO EmployeeUpdateBatches
(EmployeeId, SectionKey, SectionName, Status, RequestedBy, RequestedAt, EffectiveDate, Note)
VALUES
(@EmployeeId, 'employee-master', @SectionName, 'Open', @RequestedBy, SYSUTCDATETIME(), @EffectiveDate, @Note);

SELECT CAST(SCOPE_IDENTITY() AS int);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@EmployeeId", employeeId);
                HrmsDatabase.AddParameter(command, "@SectionName", sectionName);
                HrmsDatabase.AddParameter(command, "@RequestedBy", requestedBy);
                HrmsDatabase.AddParameter(command, "@EffectiveDate", resolvedEffectiveDate);
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

        StatusMessage = $"\u062A\u0645 \u0625\u0646\u0634\u0627\u0621 \u062D\u0631\u0643\u0629 \u063A\u064A\u0631 \u0645\u0642\u0641\u0644\u0629 \u0631\u0642\u0645 {batchId}. \u062A\u0627\u0631\u064A\u062E \u0627\u0644\u0633\u0631\u064A\u0627\u0646: {resolvedEffectiveDate:dd/MM/yyyy}.";
        return RedirectToPage(new { employeeId, tab = "confirm", section = "employee-master" });
    }
    // NEXORA_FIX14B_STAGE_METHOD_END
    public async Task<IActionResult> OnPostLockAsync(int batchId, int employeeId)
    {
        await EmployeeUpdateSchema.EnsureAsync(_dbContext);
        await EnsureMovementColumnsAsync();

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
        await EmployeeUpdateSchema.EnsureAsync(_dbContext);
        await EnsureMovementColumnsAsync();

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

    // NEXORA_FIX14B_MOVEMENT_COLUMNS_START
    private async Task EnsureMovementColumnsAsync()
    {
        await HrmsDatabase.ExecuteAsync(
            _dbContext,
            """
IF COL_LENGTH('EmployeeUpdateBatches', 'EffectiveDate') IS NULL
BEGIN
    ALTER TABLE EmployeeUpdateBatches ADD EffectiveDate date NULL;
END;
""");
    }
    // NEXORA_FIX14B_MOVEMENT_COLUMNS_END
    private async Task LoadPageAsync(int? employeeId, string? tab, string? section)
    {
        await EmployeeUpdateSchema.EnsureAsync(_dbContext);
        await EnsureMovementColumnsAsync();

        Sections = await BuildSectionsWithDynamicFieldsAsync(); // NEXORA_FIX14A_LOAD_DYNAMIC_SECTIONS
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
    ISNULL(Note, '') AS Note,
    ISNULL(EffectiveDate, CAST(RequestedAt AS date)) AS EffectiveDate
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
                EffectiveDate = HrmsDatabase.GetDateTime(reader, "EffectiveDate"),
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
    ISNULL(Note, '') AS Note,
    ISNULL(EffectiveDate, CAST(RequestedAt AS date)) AS EffectiveDate
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
                EffectiveDate = HrmsDatabase.GetDateTime(reader, "EffectiveDate"),
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
    public string DisplayDate(DateTime? value) => value.HasValue ? value.Value.ToString("dd/MM/yyyy") : "-";

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

    // NEXORA_FIX14A_DYNAMIC_PROFILE_FIELDS_METHOD_START
    private async Task<List<UpdateSection>> BuildSectionsWithDynamicFieldsAsync()
    {
        var sections = BuildSections();
        var existingKeys = sections
            .SelectMany(section => section.Fields)
            .Select(field => field.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var definitions = await HrmsDatabase.QueryAsync(
            _dbContext,
            """
IF OBJECT_ID(N'[dbo].[EmployeeProfileFieldDefinitions]', N'U') IS NULL
BEGIN
    SELECT
        CAST('' AS nvarchar(80)) AS SectionKey,
        CAST('' AS nvarchar(120)) AS FieldKey,
        CAST('' AS nvarchar(150)) AS FieldLabel,
        CAST('text' AS nvarchar(40)) AS FieldType,
        CAST(0 AS int) AS SortOrder
    WHERE 1 = 0;
END
ELSE
BEGIN
    SELECT
        ISNULL(SectionKey, '') AS SectionKey,
        ISNULL(FieldKey, '') AS FieldKey,
        ISNULL(FieldLabel, '') AS FieldLabel,
        ISNULL(FieldType, 'text') AS FieldType,
        ISNULL(SortOrder, 0) AS SortOrder
    FROM EmployeeProfileFieldDefinitions
    WHERE IsActive = 1
    ORDER BY
        CASE SectionKey
            WHEN 'basic' THEN 10
            WHEN 'personal' THEN 20
            WHEN 'job' THEN 30
            WHEN 'financial' THEN 40
            WHEN 'additional' THEN 50
            ELSE 99
        END,
        SortOrder,
        Id;
END
""",
            null,
            reader => new DynamicUpdateFieldDefinition
            {
                SectionKey = HrmsDatabase.GetString(reader, "SectionKey"),
                FieldKey = HrmsDatabase.GetString(reader, "FieldKey"),
                FieldLabel = HrmsDatabase.GetString(reader, "FieldLabel"),
                FieldType = HrmsDatabase.GetString(reader, "FieldType"),
                SortOrder = HrmsDatabase.GetInt(reader, "SortOrder")
            });

        foreach (var group in definitions
            .Where(field => !string.IsNullOrWhiteSpace(field.FieldKey))
            .Where(field => !existingKeys.Contains(field.FieldKey))
            .GroupBy(field => NormalizeProfileSectionKey(field.SectionKey), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => ProfileSectionOrder(group.Key)))
        {
            var fields = group
                .OrderBy(field => field.SortOrder)
                .ThenBy(field => field.FieldLabel)
                .Select(field => new UpdateField(
                    field.FieldKey,
                    string.IsNullOrWhiteSpace(field.FieldLabel) ? field.FieldKey : field.FieldLabel,
                    "custom",
                    NormalizeDynamicFieldInputType(field.FieldType),
                    string.Empty))
                .ToList();

            if (fields.Count > 0)
            {
                sections.Add(new UpdateSection(
                    "profile-" + group.Key,
                    ProfileSectionName(group.Key),
                    ProfileSectionDescription(group.Key),
                    fields));
            }
        }

        return sections;
    }

    private static string NormalizeProfileSectionKey(string? key)
    {
        return (key ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "basic" => "basic",
            "personal" => "personal",
            "job" => "job",
            "financial" => "financial",
            "additional" => "additional",
            _ => "additional"
        };
    }

    private static int ProfileSectionOrder(string key)
    {
        return key switch
        {
            "basic" => 10,
            "personal" => 20,
            "job" => 30,
            "financial" => 40,
            "additional" => 50,
            _ => 99
        };
    }

    private static string ProfileSectionName(string key)
    {
        return key switch
        {
            "basic" => "\u0627\u0644\u0628\u064A\u0627\u0646\u0627\u062A \u0627\u0644\u0623\u0633\u0627\u0633\u064A\u0629",
            "personal" => "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0634\u062E\u0635\u064A\u0629",
            "job" => "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0648\u0638\u064A\u0641\u064A\u0629",
            "financial" => "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0645\u0627\u0644\u064A\u0629",
            _ => "\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0625\u0636\u0627\u0641\u064A\u0629"
        };
    }

    private static string ProfileSectionDescription(string key)
    {
        return key switch
        {
            "basic" => "\u062D\u0642\u0648\u0644 \u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641 \u0627\u0644\u062F\u064A\u0646\u0627\u0645\u064A\u0643\u064A\u0629 \u0636\u0645\u0646 \u0627\u0644\u0628\u064A\u0627\u0646\u0627\u062A \u0627\u0644\u0623\u0633\u0627\u0633\u064A\u0629",
            "personal" => "\u062D\u0642\u0648\u0644 \u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641 \u0627\u0644\u062F\u064A\u0646\u0627\u0645\u064A\u0643\u064A\u0629 \u0636\u0645\u0646 \u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0634\u062E\u0635\u064A\u0629",
            "job" => "\u062D\u0642\u0648\u0644 \u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641 \u0627\u0644\u062F\u064A\u0646\u0627\u0645\u064A\u0643\u064A\u0629 \u0636\u0645\u0646 \u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0648\u0638\u064A\u0641\u064A\u0629",
            "financial" => "\u062D\u0642\u0648\u0644 \u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641 \u0627\u0644\u062F\u064A\u0646\u0627\u0645\u064A\u0643\u064A\u0629 \u0636\u0645\u0646 \u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0645\u0627\u0644\u064A\u0629",
            _ => "\u062D\u0642\u0648\u0644 \u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641 \u0627\u0644\u062F\u064A\u0646\u0627\u0645\u064A\u0643\u064A\u0629 \u0627\u0644\u0625\u0636\u0627\u0641\u064A\u0629"
        };
    }

    private static string NormalizeDynamicFieldInputType(string? type)
    {
        return (type ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "number" => "number",
            "date" => "date",
            "textarea" => "textarea",
            _ => "text"
        };
    }
    // NEXORA_FIX14A_DYNAMIC_PROFILE_FIELDS_METHOD_END
    // NEXORA_FIX14C_STAGE_BLOCKS_START
    private static List<UpdateSection> BuildStageBlocks(List<UpdateSection> sections)
    {
        var result = new List<UpdateSection>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        UpdateSection? FindSection(string key) =>
            sections.FirstOrDefault(section => section.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        void AddField(List<UpdateField> target, UpdateField? field)
        {
            if (field is null)
            {
                return;
            }

            if (used.Add(field.Key))
            {
                target.Add(field);
            }
        }

        void AddByKeys(List<UpdateField> target, string sectionKey, params string[] keys)
        {
            var source = FindSection(sectionKey);
            if (source is null)
            {
                return;
            }

            foreach (var key in keys)
            {
                AddField(target, source.Fields.FirstOrDefault(field => field.Key.Equals(key, StringComparison.OrdinalIgnoreCase)));
            }
        }

        void AddFromSection(List<UpdateField> target, string sectionKey)
        {
            var source = FindSection(sectionKey);
            if (source is null)
            {
                return;
            }

            foreach (var field in source.Fields)
            {
                AddField(target, field);
            }
        }

        var basic = new List<UpdateField>();
        AddByKeys(basic, "employee-info", "EmployeeNo", "FullName", "NationalId", "BirthDate", "IsActive");
        AddFromSection(basic, "profile-basic");
        if (basic.Count > 0)
        {
            result.Add(new UpdateSection(
                "stage-basic",
                "\u0627\u0644\u0628\u064A\u0627\u0646\u0627\u062A \u0627\u0644\u0623\u0633\u0627\u0633\u064A\u0629",
                "\u0627\u0644\u062D\u0642\u0648\u0644 \u0627\u0644\u0623\u0633\u0627\u0633\u064A\u0629 \u0645\u0646 \u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641.",
                basic));
        }

        var personal = new List<UpdateField>();
        AddByKeys(personal, "employee-info", "Phone", "Email");
        AddByKeys(personal, "extra", "Nationality", "Accommodation", "EmergencyContact");
        AddFromSection(personal, "profile-personal");
        if (personal.Count > 0)
        {
            result.Add(new UpdateSection(
                "stage-personal",
                "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0634\u062E\u0635\u064A\u0629",
                "\u062D\u0642\u0648\u0644 \u0627\u0644\u062A\u0648\u0627\u0635\u0644 \u0648\u0627\u0644\u0628\u064A\u0627\u0646\u0627\u062A \u0627\u0644\u0634\u062E\u0635\u064A\u0629.",
                personal));
        }

        var job = new List<UpdateField>();
        AddByKeys(job, "employee-info", "Position", "DepartmentId", "ManagerName", "HireDate");
        AddByKeys(job, "extra", "ContractType");
        AddFromSection(job, "profile-job");
        if (job.Count > 0)
        {
            result.Add(new UpdateSection(
                "stage-job",
                "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0648\u0638\u064A\u0641\u064A\u0629",
                "\u0627\u0644\u0645\u0646\u0635\u0628 \u0648\u0627\u0644\u0642\u0633\u0645 \u0648\u062A\u0641\u0627\u0635\u064A\u0644 \u0627\u0644\u062A\u0648\u0638\u064A\u0641.",
                job));
        }

        var financial = new List<UpdateField>();
        AddFromSection(financial, "financial");
        AddFromSection(financial, "payment");
        AddFromSection(financial, "profile-financial");
        if (financial.Count > 0)
        {
            result.Add(new UpdateSection(
                "stage-financial",
                "\u0627\u0644\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0627\u0644\u0645\u0627\u0644\u064A\u0629",
                "\u0627\u0644\u0631\u0627\u062A\u0628 \u0648\u0627\u0644\u062F\u0641\u0639 \u0648\u0627\u0644\u0628\u062F\u0644\u0627\u062A \u0648\u0627\u0644\u0627\u0633\u062A\u0642\u0637\u0627\u0639\u0627\u062A.",
                financial));
        }

        var additional = new List<UpdateField>();
        AddFromSection(additional, "profile-additional");
        AddFromSection(additional, "extra");

        foreach (var section in sections)
        {
            foreach (var field in section.Fields)
            {
                AddField(additional, field);
            }
        }

        if (additional.Count > 0)
        {
            result.Add(new UpdateSection(
                "stage-additional",
                "\u0645\u0639\u0644\u0648\u0645\u0627\u062A \u0625\u0636\u0627\u0641\u064A\u0629",
                "\u0623\u064A \u062D\u0642\u0648\u0644 \u0623\u062E\u0631\u0649 \u0645\u0631\u062A\u0628\u0637\u0629 \u0628\u0645\u0644\u0641 \u0627\u0644\u0645\u0648\u0638\u0641.",
                additional));
        }

        if (result.Count == 0)
        {
            var fallback = FindSection("employee-info") ?? sections.First();
            result.Add(fallback);
        }

        return result;
    }
    // NEXORA_FIX14C_STAGE_BLOCKS_END
    private Dictionary<string, UpdateField> BuildFieldDictionary()
    {
        return BuildSections()
            .SelectMany(x => x.Fields)
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }

    // NEXORA_FIX14A_DYNAMIC_PROFILE_FIELD_RECORD_START
    private sealed class DynamicUpdateFieldDefinition
    {
        public string SectionKey { get; set; } = string.Empty;
        public string FieldKey { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public string FieldType { get; set; } = "text";
        public int SortOrder { get; set; }
    }
    // NEXORA_FIX14A_DYNAMIC_PROFILE_FIELD_RECORD_END
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
        public DateTime? EffectiveDate { get; set; }
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

