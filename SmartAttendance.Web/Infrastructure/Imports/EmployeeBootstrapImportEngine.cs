using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartAttendance.Application.MasterDataImports.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Imports;

/// <summary>
/// محرك الاستيراد الشامل للموظفين: يقرأ ملفات Excel/CSV ويؤسس الموظفين مع
/// فروعهم وأقسامهم ومناصبهم دفعة واحدة (إنشاء المراجع الناقصة تلقائياً).
/// يُستخدم من صفحة /Employees/Import — أثقل ملف بالمشروع، عدّل بحذر.
/// </summary>
public sealed class EmployeeBootstrapImportEngine
{
    public const long MaxFileBytes = 10L * 1024L * 1024L;
    public const int MaxRows = 10000;

    public static IReadOnlyList<string> RequiredColumnNames { get; } =
        new[]
        {
            "EmployeeNo",
            "FullName",
            "CompanyName",
            "WorkLocationName",
            "DepartmentName",
            "PositionName",
            "HireDate"
        };

    private static readonly EmployeeTemplateColumn[] BaseColumns =
    {
        new("EmployeeNo", true, EmployeeTemplateColumnKind.Text, 16),
        new("FullName", true, EmployeeTemplateColumnKind.Text, 30),
        new("CompanyName", true, EmployeeTemplateColumnKind.Text, 28),
        new("CompanyCode", false, EmployeeTemplateColumnKind.Text, 16),
        new("WorkLocationName", true, EmployeeTemplateColumnKind.Text, 26),
        new("WorkLocationCode", false, EmployeeTemplateColumnKind.Text, 18),
        new("DepartmentName", true, EmployeeTemplateColumnKind.Text, 24),
        new("DepartmentCode", false, EmployeeTemplateColumnKind.Text, 17),
        new("PositionName", true, EmployeeTemplateColumnKind.Text, 28),
        new("PositionCode", false, EmployeeTemplateColumnKind.Text, 16),
        new("HireDate", true, EmployeeTemplateColumnKind.Date, 15),
        new("NationalId", false, EmployeeTemplateColumnKind.Text, 20),
        new("Phone", false, EmployeeTemplateColumnKind.Text, 18),
        new("Email", false, EmployeeTemplateColumnKind.Text, 28),
        new("BirthDate", false, EmployeeTemplateColumnKind.Date, 15),
        new("Gender", false, EmployeeTemplateColumnKind.Text, 14),
        new("MaritalStatus", false, EmployeeTemplateColumnKind.Text, 17),
        new("Nationality", false, EmployeeTemplateColumnKind.Text, 18),
        new("Country", false, EmployeeTemplateColumnKind.Text, 18),
        new("ContractType", false, EmployeeTemplateColumnKind.Text, 18),
        new("ContractEndDate", false, EmployeeTemplateColumnKind.Date, 17),
        new("EmploymentStatus", false, EmployeeTemplateColumnKind.Text, 18),
        new("IsActive", false, EmployeeTemplateColumnKind.Text, 13),
        new("DirectManagerEmployeeNo", false, EmployeeTemplateColumnKind.Text, 24)
    };

    private readonly ApplicationDbContext _dbContext;

    public EmployeeBootstrapImportEngine(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<string>> GetTemplateColumnsAsync()
    {
        var columns = BaseColumns
            .Select(column => column.Name)
            .ToList();

        var dynamicFields = await LoadDynamicFieldDefinitionsAsync();

        foreach (var field in dynamicFields)
        {
            columns.Add(BuildDynamicHeader(field, columns));
        }

        return columns;
    }

    public async Task<List<string>> GetRequiredTemplateColumnsAsync()
    {
        var required = RequiredColumnNames.ToList();
        var usedHeaders = new HashSet<string>(
            BaseColumns.Select(column => column.Name),
            StringComparer.OrdinalIgnoreCase);
        var dynamicFields =
            await LoadDynamicFieldDefinitionsAsync();

        foreach (var field in dynamicFields)
        {
            var header = BuildDynamicHeader(
                field,
                usedHeaders);
            usedHeaders.Add(header);

            if (field.IsRequired)
            {
                required.Add(header);
            }
        }

        return required;
    }

    public async Task<byte[]> BuildTemplateWorkbookAsync()
    {
        await EmployeeProfileDynamicFields.EnsureSchemaAsync(_dbContext);

        var columns = BaseColumns.ToList();
        var dynamicFields = await LoadDynamicFieldDefinitionsAsync();
        var usedHeaders = new HashSet<string>(
            columns.Select(column => column.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var field in dynamicFields)
        {
            var header = BuildDynamicHeader(field, usedHeaders);
            usedHeaders.Add(header);
            columns.Add(
                new EmployeeTemplateColumn(
                    header,
                    field.IsRequired,
                    EmployeeTemplateColumnKind.Custom,
                    24));
        }

        var references = await LoadTemplateReferencesAsync();

        return BuildWorkbook(columns, references);
    }

    public async Task<MasterDataImportPreviewViewModel> PreviewAsync(
        string filePath,
        string token,
        string originalFileName,
        int previewLimit)
    {
        var plan = await BuildPlanAsync(filePath);

        return new MasterDataImportPreviewViewModel
        {
            Token = token,
            FileName = originalFileName,
            ImportType = "Employees",
            TotalRows = plan.Rows.Count,
            ReadyCount = plan.Rows.Count(row => row.CanImport),
            ErrorCount = plan.Rows.Count(row => !row.CanImport),
            CreateCount = plan.Rows.Count(row =>
                row.CanImport &&
                row.EmployeeAction == "Create"),
            UpdateCount = plan.Rows.Count(row =>
                row.CanImport &&
                row.EmployeeAction == "Update"),
            PreviewLimit = previewLimit,
            Rows = plan.Rows
                .OrderBy(row => row.CanImport)
                .ThenBy(row => row.RowNumber)
                .Take(previewLimit)
                .Select(ToPreviewRow)
                .ToList()
        };
    }

    public async Task<MasterDataImportResultViewModel> ImportAsync(
        string filePath,
        string originalFileName)
    {
        await HrmsDatabase.EnsureCreatedAsync(_dbContext);
        await EmployeeProfileDynamicFields.EnsureSchemaAsync(_dbContext);

        var plan = await BuildPlanAsync(filePath);
        var validRows = plan.Rows
            .Where(row => row.CanImport)
            .OrderBy(row => row.RowNumber)
            .ToList();

        if (validRows.Count == 0)
        {
            return new MasterDataImportResultViewModel
            {
                CreatedCount = 0,
                UpdatedCount = 0,
                SkippedCount = plan.Rows.Count,
                ErrorCount = plan.Rows.Count,
                Message = "No valid employee rows were found."
            };
        }

        var dynamicDefinitions =
            await LoadDynamicFieldDefinitionsAsync();

        var structureCounts = new BootstrapStructureCounts();
        var createdEmployees = 0;
        var updatedEmployees = 0;
        var dynamicValues = 0;

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync();

        try
        {
            var companies = await _dbContext.Companies
                .Where(company => !company.IsDeleted)
                .ToListAsync();

            var branches = await _dbContext.Branches
                .Where(branch => !branch.IsDeleted)
                .ToListAsync();

            var departments = await _dbContext.Departments
                .Where(department => !department.IsDeleted)
                .ToListAsync();

            var employees = await _dbContext.Employees
                .Where(employee => !employee.IsDeleted)
                .ToListAsync();

            var positions = await LoadPositionsAsync();
            var companyCodes = new HashSet<string>(
                companies.Select(company => NormalizeCode(company.Code)),
                StringComparer.OrdinalIgnoreCase);
            var branchCodes = new HashSet<string>(
                branches.Select(branch => NormalizeCode(branch.Code)),
                StringComparer.OrdinalIgnoreCase);
            var departmentCodes = new HashSet<string>(
                departments.Select(department => NormalizeCode(department.Code)),
                StringComparer.OrdinalIgnoreCase);
            var positionCodes = new HashSet<string>(
                positions
                    .Select(position => NormalizeCode(position.Code))
                    .Where(code => !string.IsNullOrWhiteSpace(code)),
                StringComparer.OrdinalIgnoreCase);

            var resolvedRows = new List<ResolvedEmployeeImportRow>();

            foreach (var row in validRows)
            {
                var company = await EnsureCompanyAsync(
                    row,
                    companies,
                    companyCodes,
                    structureCounts);

                var branch = await EnsureBranchAsync(
                    row,
                    company,
                    branches,
                    branchCodes,
                    structureCounts);

                var department = await EnsureDepartmentAsync(
                    row,
                    company,
                    departments,
                    departmentCodes,
                    structureCounts);

                var position = await EnsurePositionAsync(
                    row,
                    company,
                    department,
                    positions,
                    positionCodes,
                    structureCounts);

                var employee = employees.FirstOrDefault(item =>
                    Same(item.EmployeeNo, row.EmployeeNo));

                var created = employee == null;
                employee ??= new Employee();

                employee.EmployeeNo = NormalizeIdentifier(row.EmployeeNo);
                employee.FullName = row.FullName.Trim();
                employee.BranchId = branch.Id;
                employee.DepartmentId = department.Id;
                employee.PositionId = position.Id;
                employee.Position = position.Name;
                employee.HireDate = row.HireDate!.Value;

                ApplyOptional(
                    row.NationalId,
                    created,
                    value => employee.NationalId = value);
                ApplyOptional(
                    row.Phone,
                    created,
                    value => employee.Phone = value);
                ApplyOptional(
                    row.Email,
                    created,
                    value => employee.Email = value);
                ApplyOptional(
                    row.Country,
                    created,
                    value => employee.Country = value);
                ApplyOptional(
                    row.Nationality,
                    created,
                    value => employee.Nationality = value);
                ApplyOptional(
                    row.Gender,
                    created,
                    value => employee.Gender = value);
                ApplyOptional(
                    row.MaritalStatus,
                    created,
                    value => employee.MaritalStatus = value);

                if (row.BirthDate.HasValue)
                {
                    employee.BirthDate = row.BirthDate;
                }

                if (row.IsActive.HasValue)
                {
                    employee.IsActive = row.IsActive.Value;
                }
                else if (created)
                {
                    employee.IsActive = true;
                }

                if (created)
                {
                    await _dbContext.Employees.AddAsync(employee);
                    employees.Add(employee);
                    createdEmployees++;
                }
                else
                {
                    updatedEmployees++;
                }

                resolvedRows.Add(
                    new ResolvedEmployeeImportRow(
                        row,
                        employee,
                        created));
            }

            await _dbContext.SaveChangesAsync();

            var employeesByNo = employees
                .Where(employee => employee.Id > 0)
                .GroupBy(
                    employee => NormalizeKey(employee.EmployeeNo),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var resolved in resolvedRows)
            {
                await UpdateExtendedEmployeeColumnsAsync(
                    resolved.Employee.Id,
                    resolved.Plan,
                    resolved.Created);

                if (!string.IsNullOrWhiteSpace(
                        resolved.Plan.DirectManagerEmployeeNo))
                {
                    var managerKey = NormalizeKey(
                        resolved.Plan.DirectManagerEmployeeNo);

                    if (employeesByNo.TryGetValue(
                            managerKey,
                            out var manager))
                    {
                        await ExecuteSqlAsync(
                            """
                            UPDATE dbo.Employees
                            SET DirectManagerId = @DirectManagerId,
                                UpdatedAt = SYSUTCDATETIME()
                            WHERE Id = @EmployeeId;
                            """,
                            command =>
                            {
                                AddParameter(
                                    command,
                                    "@DirectManagerId",
                                    manager.Id);
                                AddParameter(
                                    command,
                                    "@EmployeeId",
                                    resolved.Employee.Id);
                            });
                    }
                }

                dynamicValues +=
                    await SaveDynamicFieldsAsync(
                        resolved.Employee.Id,
                        resolved.Plan.Values,
                        dynamicDefinitions);
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        var skipped = plan.Rows.Count - validRows.Count;

        return new MasterDataImportResultViewModel
        {
            CreatedCount = createdEmployees,
            UpdatedCount = updatedEmployees,
            SkippedCount = skipped,
            ErrorCount = skipped,
            Message =
                $"Import completed from {originalFileName}. " +
                $"Employees created: {createdEmployees}, " +
                $"employees updated: {updatedEmployees}, " +
                $"companies created: {structureCounts.Companies}, " +
                $"work locations created: {structureCounts.Branches}, " +
                $"departments created: {structureCounts.Departments}, " +
                $"positions created: {structureCounts.Positions}, " +
                $"custom values saved: {dynamicValues}, " +
                $"skipped rows: {skipped}."
        };
    }

    private async Task<EmployeeBootstrapPlan> BuildPlanAsync(
        string filePath)
    {
        var file = ReadFile(filePath);
        ValidateHeaders(file.Headers);

        if (file.Rows.Count > MaxRows)
        {
            throw new InvalidOperationException(
                $"The file contains more than {MaxRows} rows.");
        }

        var dataRows = file.Rows
            .Where(HasInputData)
            .ToList();

        var snapshot = await LoadSnapshotAsync();
        var dynamicDefinitions =
            await LoadDynamicFieldDefinitionsAsync();
        var plans = new List<EmployeeBootstrapRowPlan>();
        var seenEmployeeNumbers = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in dataRows)
        {
            var plan = BuildRowPlan(
                row,
                snapshot,
                seenEmployeeNumbers,
                dynamicDefinitions);

            plans.Add(plan);
        }

        var availableManagers = new HashSet<string>(
            snapshot.EmployeeNumbers,
            StringComparer.OrdinalIgnoreCase);

        foreach (var plan in plans.Where(item => item.CanImport))
        {
            availableManagers.Add(
                NormalizeKey(plan.EmployeeNo));
        }

        foreach (var plan in plans)
        {
            if (string.IsNullOrWhiteSpace(
                    plan.DirectManagerEmployeeNo))
            {
                continue;
            }

            if (Same(
                    plan.EmployeeNo,
                    plan.DirectManagerEmployeeNo))
            {
                plan.Errors.Add(
                    "The employee cannot be their own direct manager.");
                continue;
            }

            if (!availableManagers.Contains(
                    NormalizeKey(
                        plan.DirectManagerEmployeeNo)))
            {
                plan.Errors.Add(
                    $"Direct manager employee number was not found: " +
                    $"{plan.DirectManagerEmployeeNo}");
            }
        }

        return new EmployeeBootstrapPlan(plans);
    }

    private EmployeeBootstrapRowPlan BuildRowPlan(
        ParsedImportRow row,
        BootstrapSnapshot snapshot,
        HashSet<string> seenEmployeeNumbers,
        IReadOnlyList<DynamicFieldDefinition> dynamicDefinitions)
    {
        var plan = new EmployeeBootstrapRowPlan
        {
            RowNumber = row.RowNumber,
            Values = row.Values,
            EmployeeNo = GetValue(row.Values, "EmployeeNo"),
            FullName = GetValue(row.Values, "FullName"),
            CompanyName = GetValue(
                row.Values,
                "CompanyName",
                "Company"),
            CompanyCode = GetValue(
                row.Values,
                "CompanyCode"),
            WorkLocationName = GetValue(
                row.Values,
                "WorkLocationName",
                "BranchName",
                "WorkLocation"),
            WorkLocationCode = GetValue(
                row.Values,
                "WorkLocationCode",
                "BranchCode"),
            DepartmentName = GetValue(
                row.Values,
                "DepartmentName"),
            DepartmentCode = GetValue(
                row.Values,
                "DepartmentCode"),
            PositionName = GetValue(
                row.Values,
                "PositionName",
                "Position"),
            PositionCode = GetValue(
                row.Values,
                "PositionCode"),
            NationalId = GetValue(
                row.Values,
                "NationalId"),
            Phone = GetValue(
                row.Values,
                "Phone"),
            Email = GetValue(
                row.Values,
                "Email"),
            Country = GetValue(
                row.Values,
                "Country"),
            Nationality = GetValue(
                row.Values,
                "Nationality"),
            Gender = GetValue(
                row.Values,
                "Gender"),
            MaritalStatus = GetValue(
                row.Values,
                "MaritalStatus"),
            ContractType = GetValue(
                row.Values,
                "ContractType"),
            EmploymentStatus = GetValue(
                row.Values,
                "EmploymentStatus"),
            DirectManagerEmployeeNo = GetValue(
                row.Values,
                "DirectManagerEmployeeNo")
        };

        Require(plan.EmployeeNo, "EmployeeNo", plan.Errors);
        Require(plan.FullName, "FullName", plan.Errors);
        Require(plan.CompanyName, "CompanyName", plan.Errors);
        Require(
            plan.WorkLocationName,
            "WorkLocationName",
            plan.Errors);
        Require(
            plan.DepartmentName,
            "DepartmentName",
            plan.Errors);
        Require(
            plan.PositionName,
            "PositionName",
            plan.Errors);

        foreach (var definition in dynamicDefinitions
                     .Where(item => item.IsRequired))
        {
            var matchingValue = row.Values
                .Where(pair => TryResolveDynamicHeader(
                    pair.Key,
                    dynamicDefinitions,
                    out var resolved) &&
                    Same(
                        resolved.FieldKey,
                        definition.FieldKey))
                .Select(pair => pair.Value)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(matchingValue))
            {
                plan.Errors.Add(
                    $"Required custom field is missing: " +
                    $"{definition.FieldLabel}");
            }
        }

        ValidateLength(
            plan.EmployeeNo,
            50,
            "EmployeeNo",
            plan.Errors);
        ValidateLength(
            plan.FullName,
            200,
            "FullName",
            plan.Errors);
        ValidateLength(
            plan.CompanyName,
            200,
            "CompanyName",
            plan.Errors);
        ValidateLength(
            plan.WorkLocationName,
            200,
            "WorkLocationName",
            plan.Errors);
        ValidateLength(
            plan.DepartmentName,
            200,
            "DepartmentName",
            plan.Errors);
        ValidateLength(
            plan.PositionName,
            150,
            "PositionName",
            plan.Errors);

        var hireDateText = GetValue(
            row.Values,
            "HireDate");

        if (!TryParseDate(
                hireDateText,
                out var hireDate))
        {
            plan.Errors.Add(
                $"Invalid HireDate: {hireDateText}");
        }
        else
        {
            plan.HireDate = hireDate;
        }

        var birthDateText = GetValue(
            row.Values,
            "BirthDate");

        if (!string.IsNullOrWhiteSpace(birthDateText))
        {
            if (!TryParseDate(
                    birthDateText,
                    out var birthDate))
            {
                plan.Errors.Add(
                    $"Invalid BirthDate: {birthDateText}");
            }
            else
            {
                plan.BirthDate = birthDate;
            }
        }

        var contractEndDateText = GetValue(
            row.Values,
            "ContractEndDate");

        if (!string.IsNullOrWhiteSpace(
                contractEndDateText))
        {
            if (!TryParseDate(
                    contractEndDateText,
                    out var contractEndDate))
            {
                plan.Errors.Add(
                    $"Invalid ContractEndDate: " +
                    $"{contractEndDateText}");
            }
            else
            {
                plan.ContractEndDate =
                    contractEndDate;
            }
        }

        var isActiveText = GetValue(
            row.Values,
            "IsActive");

        if (!string.IsNullOrWhiteSpace(isActiveText))
        {
            if (!TryParseBoolean(
                    isActiveText,
                    out var isActive))
            {
                plan.Errors.Add(
                    $"Invalid IsActive value: {isActiveText}");
            }
            else
            {
                plan.IsActive = isActive;
            }
        }

        if (!string.IsNullOrWhiteSpace(plan.Email) &&
            !LooksLikeEmail(plan.Email))
        {
            plan.Errors.Add(
                $"Invalid Email: {plan.Email}");
        }

        if (!string.IsNullOrWhiteSpace(plan.EmployeeNo))
        {
            var employeeKey =
                NormalizeKey(plan.EmployeeNo);

            if (!seenEmployeeNumbers.Add(employeeKey))
            {
                plan.Errors.Add(
                    "Duplicate EmployeeNo inside the same file.");
            }
        }

        var company = ResolveCompany(
            plan,
            snapshot);

        if (company != null)
        {
            plan.Company = company;

            var branch = ResolveBranch(
                plan,
                company,
                snapshot);

            if (branch != null)
            {
                plan.Branch = branch;
            }

            var department = ResolveDepartment(
                plan,
                company,
                snapshot);

            if (department != null)
            {
                plan.Department = department;
            }

            var position = ResolvePosition(
                plan,
                company,
                department,
                snapshot);

            if (position != null)
            {
                plan.Position = position;
            }
        }

        plan.EmployeeAction =
            snapshot.EmployeeNumbers.Contains(
                NormalizeKey(plan.EmployeeNo))
                ? "Update"
                : "Create";

        if (plan.EmployeeAction == "Update")
        {
            plan.Messages.Add(
                "Employee exists and will be updated. " +
                "Blank optional cells keep their current values.");
        }
        else
        {
            plan.Messages.Add(
                "Employee will be created.");
        }

        return plan;
    }

    private static CompanyReference? ResolveCompany(
        EmployeeBootstrapRowPlan plan,
        BootstrapSnapshot snapshot)
    {
        var byCode = string.IsNullOrWhiteSpace(
                plan.CompanyCode)
            ? null
            : snapshot.Companies.FirstOrDefault(
                item => Same(
                    item.Code,
                    plan.CompanyCode));

        var byName = snapshot.Companies
            .Where(item => Same(
                item.Name,
                plan.CompanyName))
            .ToList();

        if (byCode != null &&
            !Same(byCode.Name, plan.CompanyName))
        {
            plan.Errors.Add(
                $"CompanyCode {plan.CompanyCode} belongs to " +
                $"'{byCode.Name}', not '{plan.CompanyName}'.");
            return null;
        }

        if (byCode == null && byName.Count > 1)
        {
            plan.Errors.Add(
                $"Company name is ambiguous: {plan.CompanyName}. " +
                "Use CompanyCode.");
            return null;
        }

        var selected = byCode ?? byName.FirstOrDefault();

        if (selected != null)
        {
            return selected;
        }

        if (!string.IsNullOrWhiteSpace(plan.CompanyCode) &&
            snapshot.Companies.Any(item =>
                Same(item.Code, plan.CompanyCode)))
        {
            plan.Errors.Add(
                $"CompanyCode already exists: {plan.CompanyCode}");
            return null;
        }

        var planned = new CompanyReference(
            0,
            plan.CompanyName.Trim(),
            NormalizeCode(plan.CompanyCode),
            true);

        snapshot.Companies.Add(planned);
        plan.Messages.Add(
            $"Company '{planned.Name}' does not exist " +
            "and will be created automatically.");

        return planned;
    }

    private static BranchReference? ResolveBranch(
        EmployeeBootstrapRowPlan plan,
        CompanyReference company,
        BootstrapSnapshot snapshot)
    {
        var byCode = string.IsNullOrWhiteSpace(
                plan.WorkLocationCode)
            ? null
            : snapshot.Branches.FirstOrDefault(
                item => Same(
                    item.Code,
                    plan.WorkLocationCode));

        if (byCode != null &&
            !Same(byCode.CompanyKey, company.Key))
        {
            plan.Errors.Add(
                $"WorkLocationCode {plan.WorkLocationCode} " +
                "belongs to another company.");
            return null;
        }

        var byName = snapshot.Branches
            .Where(item =>
                Same(item.CompanyKey, company.Key) &&
                Same(item.Name, plan.WorkLocationName))
            .ToList();

        if (byCode != null &&
            !Same(byCode.Name, plan.WorkLocationName))
        {
            plan.Errors.Add(
                $"WorkLocationCode {plan.WorkLocationCode} " +
                $"belongs to '{byCode.Name}', not " +
                $"'{plan.WorkLocationName}'.");
            return null;
        }

        var selected = byCode ?? byName.FirstOrDefault();

        if (selected != null)
        {
            return selected;
        }

        var planned = new BranchReference(
            0,
            company.Key,
            plan.WorkLocationName.Trim(),
            NormalizeCode(plan.WorkLocationCode),
            true);

        snapshot.Branches.Add(planned);
        plan.Messages.Add(
            $"Work location '{planned.Name}' does not exist " +
            "and will be created automatically.");

        return planned;
    }

    private static DepartmentReference? ResolveDepartment(
        EmployeeBootstrapRowPlan plan,
        CompanyReference company,
        BootstrapSnapshot snapshot)
    {
        var byCode = string.IsNullOrWhiteSpace(
                plan.DepartmentCode)
            ? null
            : snapshot.Departments.FirstOrDefault(
                item => Same(
                    item.Code,
                    plan.DepartmentCode));

        if (byCode != null &&
            !Same(byCode.CompanyKey, company.Key))
        {
            plan.Errors.Add(
                $"DepartmentCode {plan.DepartmentCode} " +
                "belongs to another company.");
            return null;
        }

        var byName = snapshot.Departments
            .Where(item =>
                Same(item.CompanyKey, company.Key) &&
                Same(item.Name, plan.DepartmentName))
            .ToList();

        if (byCode != null &&
            !Same(byCode.Name, plan.DepartmentName))
        {
            plan.Errors.Add(
                $"DepartmentCode {plan.DepartmentCode} " +
                $"belongs to '{byCode.Name}', not " +
                $"'{plan.DepartmentName}'.");
            return null;
        }

        var selected = byCode ?? byName.FirstOrDefault();

        if (selected != null)
        {
            return selected;
        }

        var planned = new DepartmentReference(
            0,
            company.Key,
            plan.DepartmentName.Trim(),
            NormalizeCode(plan.DepartmentCode),
            true);

        snapshot.Departments.Add(planned);
        plan.Messages.Add(
            $"Department '{planned.Name}' does not exist " +
            "and will be created automatically.");

        return planned;
    }

    private static PositionReference? ResolvePosition(
        EmployeeBootstrapRowPlan plan,
        CompanyReference company,
        DepartmentReference? department,
        BootstrapSnapshot snapshot)
    {
        var byCode = string.IsNullOrWhiteSpace(
                plan.PositionCode)
            ? null
            : snapshot.Positions.FirstOrDefault(
                item =>
                    Same(item.CompanyKey, company.Key) &&
                    Same(item.Code, plan.PositionCode));

        var byName = snapshot.Positions
            .Where(item =>
                Same(item.CompanyKey, company.Key) &&
                Same(item.Name, plan.PositionName))
            .ToList();

        if (byCode != null &&
            !Same(byCode.Name, plan.PositionName))
        {
            plan.Errors.Add(
                $"PositionCode {plan.PositionCode} belongs to " +
                $"'{byCode.Name}', not '{plan.PositionName}'.");
            return null;
        }

        var selected = byCode ?? byName.FirstOrDefault();

        if (selected != null)
        {
            if (department != null &&
                selected.IsPlanned &&
                !string.IsNullOrWhiteSpace(
                    selected.DepartmentKey) &&
                !Same(
                    selected.DepartmentKey,
                    department.Key))
            {
                plan.Errors.Add(
                    $"Position '{plan.PositionName}' is used for " +
                    "more than one department in the same file. " +
                    "Use distinct position names.");
            }

            return selected;
        }

        var planned = new PositionReference(
            0,
            company.Key,
            department?.Key ?? string.Empty,
            plan.PositionName.Trim(),
            NormalizeCode(plan.PositionCode),
            true);

        snapshot.Positions.Add(planned);
        plan.Messages.Add(
            $"Position '{planned.Name}' does not exist " +
            "and will be created automatically.");

        return planned;
    }

    private async Task<BootstrapSnapshot> LoadSnapshotAsync()
    {
        var companies = await _dbContext.Companies
            .AsNoTracking()
            .Where(company => !company.IsDeleted)
            .Select(company => new CompanyReference(
                company.Id,
                company.Name,
                company.Code,
                false))
            .ToListAsync();

        var companyKeys = companies.ToDictionary(
            company => company.Id,
            company => company.Key);

        var branches = await _dbContext.Branches
            .AsNoTracking()
            .Where(branch => !branch.IsDeleted)
            .Select(branch => new
            {
                branch.Id,
                branch.CompanyId,
                branch.Name,
                branch.Code
            })
            .ToListAsync();

        var branchReferences = branches
            .Where(branch =>
                companyKeys.ContainsKey(branch.CompanyId))
            .Select(branch => new BranchReference(
                branch.Id,
                companyKeys[branch.CompanyId],
                branch.Name,
                branch.Code,
                false))
            .ToList();

        var departments = await _dbContext.Departments
            .AsNoTracking()
            .Where(department => !department.IsDeleted)
            .Select(department => new
            {
                department.Id,
                department.CompanyId,
                department.Name,
                department.Code
            })
            .ToListAsync();

        var departmentReferences = departments
            .Where(department =>
                companyKeys.ContainsKey(department.CompanyId))
            .Select(department => new DepartmentReference(
                department.Id,
                companyKeys[department.CompanyId],
                department.Name,
                department.Code,
                false))
            .ToList();

        var positions = await LoadPositionsAsync();

        var positionReferences = positions
            .Where(position =>
                companyKeys.ContainsKey(position.CompanyId))
            .Select(position => new PositionReference(
                position.Id,
                companyKeys[position.CompanyId],
                position.DepartmentId.HasValue
                    ? $"id:{position.DepartmentId.Value}"
                    : string.Empty,
                position.Name,
                position.Code,
                false))
            .ToList();

        var employeeNumbers = await _dbContext.Employees
            .AsNoTracking()
            .Where(employee => !employee.IsDeleted)
            .Select(employee => employee.EmployeeNo)
            .ToListAsync();

        return new BootstrapSnapshot(
            companies,
            branchReferences,
            departmentReferences,
            positionReferences,
            employeeNumbers
                .Select(NormalizeKey)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase));
    }

    private async Task<Company> EnsureCompanyAsync(
        EmployeeBootstrapRowPlan row,
        List<Company> companies,
        HashSet<string> usedCodes,
        BootstrapStructureCounts counts)
    {
        var company = ResolveExistingCompany(
            companies,
            row.CompanyName,
            row.CompanyCode);

        if (company != null)
        {
            company.IsActive = true;
            return company;
        }

        var code = SelectCode(
            row.CompanyCode,
            "COMP",
            usedCodes);

        company = new Company
        {
            Name = row.CompanyName.Trim(),
            Code = code,
            IsActive = true
        };

        await _dbContext.Companies.AddAsync(company);
        await _dbContext.SaveChangesAsync();

        companies.Add(company);
        usedCodes.Add(NormalizeCode(code));
        counts.Companies++;

        return company;
    }

    private async Task<Branch> EnsureBranchAsync(
        EmployeeBootstrapRowPlan row,
        Company company,
        List<Branch> branches,
        HashSet<string> usedCodes,
        BootstrapStructureCounts counts)
    {
        var branch = branches.FirstOrDefault(item =>
            item.CompanyId == company.Id &&
            (
                Same(item.Name, row.WorkLocationName) ||
                (
                    !string.IsNullOrWhiteSpace(
                        row.WorkLocationCode) &&
                    Same(
                        item.Code,
                        row.WorkLocationCode)
                )
            ));

        if (branch != null)
        {
            branch.IsActive = true;
            return branch;
        }

        var code = SelectCode(
            row.WorkLocationCode,
            "BR",
            usedCodes);

        branch = new Branch
        {
            CompanyId = company.Id,
            Name = row.WorkLocationName.Trim(),
            Code = code,
            IsActive = true
        };

        await _dbContext.Branches.AddAsync(branch);
        await _dbContext.SaveChangesAsync();

        branches.Add(branch);
        usedCodes.Add(NormalizeCode(code));
        counts.Branches++;

        return branch;
    }

    private async Task<Department> EnsureDepartmentAsync(
        EmployeeBootstrapRowPlan row,
        Company company,
        List<Department> departments,
        HashSet<string> usedCodes,
        BootstrapStructureCounts counts)
    {
        var department = departments.FirstOrDefault(item =>
            item.CompanyId == company.Id &&
            (
                Same(item.Name, row.DepartmentName) ||
                (
                    !string.IsNullOrWhiteSpace(
                        row.DepartmentCode) &&
                    Same(
                        item.Code,
                        row.DepartmentCode)
                )
            ));

        if (department != null)
        {
            department.IsActive = true;
            return department;
        }

        var code = SelectCode(
            row.DepartmentCode,
            "DEP",
            usedCodes);

        department = new Department
        {
            CompanyId = company.Id,
            BranchId = null,
            Name = row.DepartmentName.Trim(),
            Code = code,
            IsActive = true
        };

        await _dbContext.Departments.AddAsync(department);
        await _dbContext.SaveChangesAsync();

        departments.Add(department);
        usedCodes.Add(NormalizeCode(code));
        counts.Departments++;

        return department;
    }

    private async Task<PositionRow> EnsurePositionAsync(
        EmployeeBootstrapRowPlan row,
        Company company,
        Department department,
        List<PositionRow> positions,
        HashSet<string> usedCodes,
        BootstrapStructureCounts counts)
    {
        var position = positions.FirstOrDefault(item =>
            item.CompanyId == company.Id &&
            (
                Same(item.Name, row.PositionName) ||
                (
                    !string.IsNullOrWhiteSpace(
                        row.PositionCode) &&
                    Same(
                        item.Code,
                        row.PositionCode)
                )
            ));

        if (position != null)
        {
            await ExecuteSqlAsync(
                """
                UPDATE dbo.HrJobPositions
                SET IsActive = 1,
                    DepartmentId =
                        CASE
                            WHEN DepartmentId IS NULL
                            THEN @DepartmentId
                            ELSE DepartmentId
                        END,
                    UpdatedAt = SYSDATETIME()
                WHERE Id = @Id;
                """,
                command =>
                {
                    AddParameter(
                        command,
                        "@DepartmentId",
                        department.Id);
                    AddParameter(
                        command,
                        "@Id",
                        position.Id);
                });

            return position;
        }

        var code = SelectCode(
            row.PositionCode,
            "POS",
            usedCodes);

        var id = await ExecuteScalarIntAsync(
            """
            INSERT INTO dbo.HrJobPositions
            (
                CompanyId,
                ArabicName,
                JobCode,
                DepartmentId,
                IsActive,
                CreatedAt
            )
            VALUES
            (
                @CompanyId,
                @ArabicName,
                @JobCode,
                @DepartmentId,
                1,
                SYSDATETIME()
            );

            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,
            command =>
            {
                AddParameter(
                    command,
                    "@CompanyId",
                    company.Id);
                AddParameter(
                    command,
                    "@ArabicName",
                    row.PositionName.Trim());
                AddParameter(
                    command,
                    "@JobCode",
                    code);
                AddParameter(
                    command,
                    "@DepartmentId",
                    department.Id);
            });

        position = new PositionRow(
            id,
            company.Id,
            department.Id,
            row.PositionName.Trim(),
            code);

        positions.Add(position);
        usedCodes.Add(NormalizeCode(code));
        counts.Positions++;

        return position;
    }

    private async Task UpdateExtendedEmployeeColumnsAsync(
        int employeeId,
        EmployeeBootstrapRowPlan row,
        bool created)
    {
        var hasContractType =
            !string.IsNullOrWhiteSpace(row.ContractType);
        var hasContractEndDate =
            row.ContractEndDate.HasValue;
        var hasEmploymentStatus =
            !string.IsNullOrWhiteSpace(
                row.EmploymentStatus);

        await ExecuteSqlAsync(
            """
            UPDATE dbo.Employees
            SET ContractType =
                    CASE
                        WHEN @HasContractType = 1
                        THEN @ContractType
                        ELSE ContractType
                    END,
                ContractEndDate =
                    CASE
                        WHEN @HasContractEndDate = 1
                        THEN @ContractEndDate
                        ELSE ContractEndDate
                    END,
                EmploymentStatus =
                    CASE
                        WHEN @HasEmploymentStatus = 1
                        THEN @EmploymentStatus
                        WHEN @Created = 1
                        THEN N'Active'
                        ELSE EmploymentStatus
                    END,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @EmployeeId;
            """,
            command =>
            {
                AddParameter(
                    command,
                    "@HasContractType",
                    hasContractType);
                AddParameter(
                    command,
                    "@ContractType",
                    NullIfBlank(row.ContractType));
                AddParameter(
                    command,
                    "@HasContractEndDate",
                    hasContractEndDate);
                AddParameter(
                    command,
                    "@ContractEndDate",
                    row.ContractEndDate);
                AddParameter(
                    command,
                    "@HasEmploymentStatus",
                    hasEmploymentStatus);
                AddParameter(
                    command,
                    "@EmploymentStatus",
                    NullIfBlank(
                        row.EmploymentStatus));
                AddParameter(
                    command,
                    "@Created",
                    created);
                AddParameter(
                    command,
                    "@EmployeeId",
                    employeeId);
            });
    }

    private async Task<int> SaveDynamicFieldsAsync(
        int employeeId,
        Dictionary<string, string> values,
        List<DynamicFieldDefinition> definitions)
    {
        var saved = 0;

        foreach (var pair in values)
        {
            if (!TryResolveDynamicHeader(
                    pair.Key,
                    definitions,
                    out var definition))
            {
                continue;
            }

            var value = pair.Value?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            await ExecuteSqlAsync(
                """
                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.EmployeeCustomFields
                    WHERE EmployeeId = @EmployeeId
                      AND FieldKey = @FieldKey
                )
                BEGIN
                    UPDATE dbo.EmployeeCustomFields
                    SET FieldLabel = @FieldLabel,
                        FieldValue = @FieldValue,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE EmployeeId = @EmployeeId
                      AND FieldKey = @FieldKey;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.EmployeeCustomFields
                    (
                        EmployeeId,
                        FieldKey,
                        FieldLabel,
                        FieldValue,
                        UpdatedAt
                    )
                    VALUES
                    (
                        @EmployeeId,
                        @FieldKey,
                        @FieldLabel,
                        @FieldValue,
                        SYSUTCDATETIME()
                    );
                END;
                """,
                command =>
                {
                    AddParameter(
                        command,
                        "@EmployeeId",
                        employeeId);
                    AddParameter(
                        command,
                        "@FieldKey",
                        definition.FieldKey);
                    AddParameter(
                        command,
                        "@FieldLabel",
                        definition.FieldLabel);
                    AddParameter(
                        command,
                        "@FieldValue",
                        value);
                });

            saved++;
        }

        return saved;
    }

    private async Task<TemplateReferenceData> LoadTemplateReferencesAsync()
    {
        var companies = await _dbContext.Companies
            .AsNoTracking()
            .Where(company =>
                !company.IsDeleted &&
                company.IsActive)
            .OrderBy(company => company.Name)
            .Select(company => new TemplateReferenceRow(
                company.Name,
                company.Code,
                string.Empty))
            .ToListAsync();

        var branches = await (
                from branch in _dbContext.Branches.AsNoTracking()
                join company in _dbContext.Companies.AsNoTracking()
                    on branch.CompanyId equals company.Id
                where
                    !branch.IsDeleted &&
                    branch.IsActive &&
                    !company.IsDeleted
                orderby company.Name, branch.Name
                select new TemplateReferenceRow(
                    branch.Name,
                    branch.Code,
                    company.Name))
            .ToListAsync();

        var departments = await (
                from department in _dbContext.Departments.AsNoTracking()
                join company in _dbContext.Companies.AsNoTracking()
                    on department.CompanyId equals company.Id
                where
                    !department.IsDeleted &&
                    department.IsActive &&
                    !company.IsDeleted
                orderby company.Name, department.Name
                select new TemplateReferenceRow(
                    department.Name,
                    department.Code,
                    company.Name))
            .ToListAsync();

        var positions = await LoadPositionsAsync();

        var companyNames = await _dbContext.Companies
            .AsNoTracking()
            .Where(company => !company.IsDeleted)
            .ToDictionaryAsync(
                company => company.Id,
                company => company.Name);

        var positionReferences = positions
            .Where(position =>
                companyNames.ContainsKey(
                    position.CompanyId))
            .OrderBy(position =>
                companyNames[position.CompanyId])
            .ThenBy(position => position.Name)
            .Select(position => new TemplateReferenceRow(
                position.Name,
                position.Code,
                companyNames[position.CompanyId]))
            .ToList();

        return new TemplateReferenceData(
            companies,
            branches,
            departments,
            positionReferences);
    }

    private async Task<List<DynamicFieldDefinition>>
        LoadDynamicFieldDefinitionsAsync()
    {
        await EmployeeProfileDynamicFields.EnsureSchemaAsync(
            _dbContext);

        return await QueryAsync(
            """
            SELECT
                FieldKey,
                FieldLabel,
                IsRequired,
                SectionKey,
                SortOrder
            FROM dbo.EmployeeProfileFieldDefinitions
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
            """,
            command => { },
            reader => new DynamicFieldDefinition(
                GetString(reader, "FieldKey"),
                GetString(reader, "FieldLabel"),
                GetBoolean(reader, "IsRequired"),
                GetString(reader, "SectionKey"),
                GetInt32(reader, "SortOrder")));
    }

    private async Task<List<PositionRow>> LoadPositionsAsync()
    {
        return await QueryAsync(
            """
            IF OBJECT_ID(N'dbo.HrJobPositions', N'U') IS NULL
            BEGIN
                SELECT
                    CAST(0 AS int) AS Id,
                    CAST(0 AS int) AS CompanyId,
                    CAST(NULL AS int) AS DepartmentId,
                    CAST(N'' AS nvarchar(400)) AS ArabicName,
                    CAST(N'' AS nvarchar(160)) AS JobCode
                WHERE 1 = 0;
            END
            ELSE
            BEGIN
                SELECT
                    Id,
                    CompanyId,
                    DepartmentId,
                    ArabicName,
                    ISNULL(JobCode, N'') AS JobCode
                FROM dbo.HrJobPositions
                WHERE IsActive = 1;
            END;
            """,
            command => { },
            reader => new PositionRow(
                GetInt32(reader, "Id"),
                GetInt32(reader, "CompanyId"),
                GetNullableInt32(
                    reader,
                    "DepartmentId"),
                GetString(reader, "ArabicName"),
                GetString(reader, "JobCode")));
    }

    private static MasterDataImportPreviewRowViewModel ToPreviewRow(
        EmployeeBootstrapRowPlan row)
    {
        var messages = row.CanImport
            ? row.Messages
            : row.Errors;

        return new MasterDataImportPreviewRowViewModel
        {
            RowNumber = row.RowNumber,
            Key = row.EmployeeNo,
            Action = row.CanImport
                ? row.EmployeeAction
                : "-",
            Status = row.CanImport
                ? "Ready"
                : "Error",
            Message = string.Join(" ", messages),
            CanImport = row.CanImport,
            Values = row.Values
        };
    }

    private static bool HasInputData(ParsedImportRow row)
    {
        foreach (var pair in row.Values)
        {
            if (IsReferenceHeader(pair.Key))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReferenceHeader(string header)
    {
        return NormalizeHeader(header)
            .StartsWith(
                "ref",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateHeaders(
        IReadOnlyList<string> headers)
    {
        var normalized = headers
            .Select(NormalizeHeader)
            .ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        bool HasAny(params string[] names)
        {
            return names.Any(name =>
                normalized.Contains(
                    NormalizeHeader(name)));
        }

        var missing = new List<string>();

        if (!HasAny("EmployeeNo"))
        {
            missing.Add("EmployeeNo");
        }

        if (!HasAny("FullName"))
        {
            missing.Add("FullName");
        }

        if (!HasAny("CompanyName", "CompanyCode"))
        {
            missing.Add("CompanyName");
        }

        if (!HasAny(
                "WorkLocationName",
                "WorkLocationCode",
                "BranchName",
                "BranchCode",
                "WorkLocation"))
        {
            missing.Add("WorkLocationName");
        }

        if (!HasAny(
                "DepartmentName",
                "DepartmentCode"))
        {
            missing.Add("DepartmentName");
        }

        if (!HasAny("PositionName", "Position"))
        {
            missing.Add("PositionName");
        }

        if (!HasAny("HireDate"))
        {
            missing.Add("HireDate");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Missing required columns: " +
                string.Join(", ", missing));
        }
    }

    private static ParsedImportFile ReadFile(string filePath)
    {
        var extension = Path
            .GetExtension(filePath)
            .ToLowerInvariant();

        return extension switch
        {
            ".xlsx" => ReadXlsx(filePath),
            ".csv" => ReadCsv(filePath),
            _ => throw new InvalidOperationException(
                "Unsupported file type. Upload .xlsx or .csv.")
        };
    }

    private static ParsedImportFile ReadXlsx(string filePath)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var sharedStrings =
            ReadSharedStrings(archive);
        var worksheetPath =
            GetFirstWorksheetPath(archive);
        var entry = archive.GetEntry(worksheetPath)
            ?? throw new InvalidOperationException(
                "Worksheet not found inside Excel file.");

        XNamespace ns =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var rows = document
            .Descendants(ns + "sheetData")
            .Elements(ns + "row")
            .ToList();

        if (rows.Count == 0)
        {
            return new ParsedImportFile();
        }

        var headerCells = ReadRowCells(
            rows[0],
            sharedStrings,
            ns);

        var headers = headerCells
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .ToList();

        var result = new ParsedImportFile
        {
            Headers = headers
        };

        foreach (var row in rows.Skip(1))
        {
            var rowNumber = int.TryParse(
                row.Attribute("r")?.Value,
                out var parsedRowNumber)
                ? parsedRowNumber
                : result.Rows.Count + 2;

            var cells = ReadRowCells(
                row,
                sharedStrings,
                ns);

            if (cells.Count == 0)
            {
                continue;
            }

            var values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            for (var index = 0;
                 index < headers.Count;
                 index++)
            {
                values[headers[index]] =
                    cells.TryGetValue(
                        index,
                        out var value)
                        ? value
                        : string.Empty;
            }

            result.Rows.Add(
                new ParsedImportRow(
                    rowNumber,
                    values));
        }

        return result;
    }

    private static ParsedImportFile ReadCsv(string filePath)
    {
        var lines = File.ReadAllLines(
            filePath,
            Encoding.UTF8);

        if (lines.Length == 0)
        {
            return new ParsedImportFile();
        }

        var headers = SplitDelimitedLine(
            lines[0],
            ',');

        var result = new ParsedImportFile
        {
            Headers = headers
        };

        for (var index = 1;
             index < lines.Length;
             index++)
        {
            var cells = SplitDelimitedLine(
                lines[index],
                ',');

            var values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            for (var headerIndex = 0;
                 headerIndex < headers.Count;
                 headerIndex++)
            {
                values[headers[headerIndex]] =
                    headerIndex < cells.Count
                        ? cells[headerIndex]
                        : string.Empty;
            }

            result.Rows.Add(
                new ParsedImportRow(
                    index + 1,
                    values));
        }

        return result;
    }

    private static Dictionary<int, string> ReadRowCells(
        XElement row,
        IReadOnlyList<string> sharedStrings,
        XNamespace ns)
    {
        var values = new Dictionary<int, string>();

        foreach (var cell in row.Elements(ns + "c"))
        {
            var reference =
                cell.Attribute("r")?.Value ??
                string.Empty;
            var columnIndex =
                GetColumnIndex(reference);

            if (columnIndex < 0)
            {
                continue;
            }

            var type =
                cell.Attribute("t")?.Value;
            var rawValue =
                cell.Element(ns + "v")?.Value ??
                string.Empty;

            string value;

            if (type == "s" &&
                int.TryParse(
                    rawValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var sharedStringIndex) &&
                sharedStringIndex >= 0 &&
                sharedStringIndex <
                    sharedStrings.Count)
            {
                value =
                    sharedStrings[sharedStringIndex];
            }
            else if (type == "inlineStr")
            {
                value = string.Concat(
                    cell.Descendants(ns + "t")
                        .Select(text => text.Value));
            }
            else
            {
                value = rawValue;
            }

            values[columnIndex] =
                value?.Trim() ?? string.Empty;
        }

        return values;
    }

    private static List<string> ReadSharedStrings(
        ZipArchive archive)
    {
        var entry = archive.GetEntry(
            "xl/sharedStrings.xml");

        if (entry == null)
        {
            return new List<string>();
        }

        XNamespace ns =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        return document
            .Descendants(ns + "si")
            .Select(item => string.Concat(
                item.Descendants(ns + "t")
                    .Select(text => text.Value)))
            .ToList();
    }

    private static string GetFirstWorksheetPath(
        ZipArchive archive)
    {
        XNamespace mainNs =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookEntry = archive.GetEntry(
            "xl/workbook.xml")
            ?? throw new InvalidOperationException(
                "workbook.xml not found.");

        var relationshipsEntry = archive.GetEntry(
            "xl/_rels/workbook.xml.rels")
            ?? throw new InvalidOperationException(
                "workbook relationships not found.");

        using var workbookStream =
            workbookEntry.Open();
        var workbookDocument =
            XDocument.Load(workbookStream);

        var firstSheet = workbookDocument
            .Descendants(mainNs + "sheet")
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No sheets found in workbook.");

        var relationshipId =
            firstSheet.Attribute(relNs + "id")
                ?.Value;

        using var relationshipsStream =
            relationshipsEntry.Open();
        var relationshipsDocument =
            XDocument.Load(relationshipsStream);

        var relationship =
            relationshipsDocument
                .Descendants(
                    packageRelNs + "Relationship")
                .FirstOrDefault(item =>
                    item.Attribute("Id")?.Value ==
                    relationshipId)
            ?? throw new InvalidOperationException(
                "Worksheet relationship not found.");

        var target =
            relationship.Attribute("Target")?.Value
            ?? throw new InvalidOperationException(
                "Worksheet target not found.");

        if (target.StartsWith(
                "/",
                StringComparison.Ordinal))
        {
            return target.TrimStart('/');
        }

        return "xl/" + target.TrimStart('/');
    }

    private static int GetColumnIndex(
        string reference)
    {
        var letters = new string(
            reference
                .TakeWhile(char.IsLetter)
                .ToArray());

        if (string.IsNullOrWhiteSpace(letters))
        {
            return -1;
        }

        var index = 0;

        foreach (var letter in letters
                     .ToUpperInvariant())
        {
            index *= 26;
            index += letter - 'A' + 1;
        }

        return index - 1;
    }

    private static List<string> SplitDelimitedLine(
        string line,
        char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var insideQuotes = false;

        for (var index = 0;
             index < line.Length;
             index++)
        {
            var character = line[index];

            if (character == '"')
            {
                if (insideQuotes &&
                    index + 1 < line.Length &&
                    line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (
                character == delimiter &&
                !insideQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        result.Add(current.ToString());
        return result;
    }

    private static byte[] BuildWorkbook(
        IReadOnlyList<EmployeeTemplateColumn> dataColumns,
        TemplateReferenceData references)
    {
        var referenceColumns =
            BuildReferenceColumns(references);
        var firstReferenceColumn =
            dataColumns.Count + 3;
        var totalColumns =
            firstReferenceColumn +
            referenceColumns.Count - 1;
        var maxReferenceRows =
            referenceColumns.Count == 0
                ? 1
                : referenceColumns.Max(column =>
                    column.Values.Count) + 1;
        var maxRow = Math.Max(5000, maxReferenceRows);

        using var memory = new MemoryStream();

        using (var archive = new ZipArchive(
                   memory,
                   ZipArchiveMode.Create,
                   true))
        {
            AddZipEntry(
                archive,
                "[Content_Types].xml",
                BuildContentTypesXml());
            AddZipEntry(
                archive,
                "_rels/.rels",
                BuildRootRelationshipsXml());
            AddZipEntry(
                archive,
                "xl/workbook.xml",
                BuildWorkbookXml());
            AddZipEntry(
                archive,
                "xl/_rels/workbook.xml.rels",
                BuildWorkbookRelationshipsXml());
            AddZipEntry(
                archive,
                "xl/styles.xml",
                BuildStylesXml());
            AddZipEntry(
                archive,
                "xl/worksheets/sheet1.xml",
                BuildWorksheetXml(
                    dataColumns,
                    referenceColumns,
                    firstReferenceColumn,
                    totalColumns,
                    maxReferenceRows,
                    maxRow));
        }

        return memory.ToArray();
    }

    private static List<ReferenceColumn> BuildReferenceColumns(
        TemplateReferenceData references)
    {
        return new List<ReferenceColumn>
        {
            new(
                "Ref Company Name",
                references.Companies
                    .Select(item => item.Name)
                    .ToList()),
            new(
                "Ref Company Code",
                references.Companies
                    .Select(item => item.Code)
                    .ToList()),
            new(
                "Ref Work Location Name",
                references.Branches
                    .Select(item => item.Name)
                    .ToList()),
            new(
                "Ref Work Location Code",
                references.Branches
                    .Select(item => item.Code)
                    .ToList()),
            new(
                "Ref Work Location Company",
                references.Branches
                    .Select(item => item.Company)
                    .ToList()),
            new(
                "Ref Department Name",
                references.Departments
                    .Select(item => item.Name)
                    .ToList()),
            new(
                "Ref Department Code",
                references.Departments
                    .Select(item => item.Code)
                    .ToList()),
            new(
                "Ref Department Company",
                references.Departments
                    .Select(item => item.Company)
                    .ToList()),
            new(
                "Ref Position Name",
                references.Positions
                    .Select(item => item.Name)
                    .ToList()),
            new(
                "Ref Position Code",
                references.Positions
                    .Select(item => item.Code)
                    .ToList()),
            new(
                "Ref Position Company",
                references.Positions
                    .Select(item => item.Company)
                    .ToList())
        };
    }

    private static string BuildContentTypesXml()
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>";
    }

    private static string BuildRootRelationshipsXml()
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";
    }

    private static string BuildWorkbookXml()
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Employee Information\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
            "</workbook>";
    }

    private static string BuildWorkbookRelationshipsXml()
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";
    }

    private static string BuildStylesXml()
    {
        return
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd\"/></numFmts>" +
            "<fonts count=\"5\">" +
            "<font><sz val=\"11\"/><name val=\"Aptos\"/></font>" +
            "<font><b/><color rgb=\"FF9F1D2D\"/><sz val=\"11\"/><name val=\"Aptos\"/></font>" +
            "<font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"11\"/><name val=\"Aptos\"/></font>" +
            "<font><b/><color rgb=\"FF7A4E00\"/><sz val=\"11\"/><name val=\"Aptos\"/></font>" +
            "<font><b/><color rgb=\"FF23364D\"/><sz val=\"11\"/><name val=\"Aptos\"/></font>" +
            "</fonts>" +
            "<fills count=\"6\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFDE2E2\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF0B3048\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFF1CC\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE8EEF5\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "</fills>" +
            "<borders count=\"2\">" +
            "<border><left/><right/><top/><bottom/><diagonal/></border>" +
            "<border>" +
            "<left style=\"thin\"><color rgb=\"FFB9C7D6\"/></left>" +
            "<right style=\"thin\"><color rgb=\"FFB9C7D6\"/></right>" +
            "<top style=\"thin\"><color rgb=\"FFB9C7D6\"/></top>" +
            "<bottom style=\"thin\"><color rgb=\"FFB9C7D6\"/></bottom>" +
            "<diagonal/>" +
            "</border>" +
            "</borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"7\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"5\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            "<xf numFmtId=\"49\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\"/>" +
            "</cellXfs>" +
            "</styleSheet>";
    }

    private static string BuildWorksheetXml(
        IReadOnlyList<EmployeeTemplateColumn> dataColumns,
        IReadOnlyList<ReferenceColumn> referenceColumns,
        int firstReferenceColumn,
        int totalColumns,
        int maxReferenceRows,
        int maxRow)
    {
        var builder = new StringBuilder();
        var dataEndColumn = dataColumns.Count;
        var dataEndReference =
            GetCellReference(1, dataEndColumn);
        var totalEndReference =
            GetCellReference(
                Math.Max(1, maxReferenceRows),
                totalColumns);

        builder.Append(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        builder.Append(
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        builder.Append("<dimension ref=\"A1:");
        builder.Append(totalEndReference);
        builder.Append("\"/>");
        builder.Append(
            "<sheetViews><sheetView workbookViewId=\"0\" rightToLeft=\"1\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        builder.Append(
            "<sheetFormatPr defaultRowHeight=\"20\"/>");
        builder.Append("<cols>");

        for (var index = 0;
             index < dataColumns.Count;
             index++)
        {
            builder.Append("<col min=\"");
            builder.Append(index + 1);
            builder.Append("\" max=\"");
            builder.Append(index + 1);
            builder.Append("\" width=\"");
            builder.Append(
                dataColumns[index].Width.ToString(
                    CultureInfo.InvariantCulture));
            builder.Append("\" style=\"");
            builder.Append(
                dataColumns[index].Kind ==
                EmployeeTemplateColumnKind.Date
                    ? 6
                    : 5);
            builder.Append("\" customWidth=\"1\"/>");
        }

        builder.Append("<col min=\"");
        builder.Append(firstReferenceColumn);
        builder.Append("\" max=\"");
        builder.Append(totalColumns);
        builder.Append(
            "\" width=\"24\" style=\"5\" customWidth=\"1\"/>");
        builder.Append("</cols>");
        builder.Append("<sheetData>");
        builder.Append("<row r=\"1\" ht=\"34\" customHeight=\"1\">");

        for (var index = 0;
             index < dataColumns.Count;
             index++)
        {
            var column = dataColumns[index];
            var style = column.Kind ==
                EmployeeTemplateColumnKind.Custom
                ? 3
                : column.Required
                    ? 1
                    : 2;
            var header = column.Required
                ? column.Name + " *"
                : column.Name;

            builder.Append(
                BuildInlineCell(
                    1,
                    index + 1,
                    header,
                    style));
        }

        for (var index = 0;
             index < referenceColumns.Count;
             index++)
        {
            builder.Append(
                BuildInlineCell(
                    1,
                    firstReferenceColumn + index,
                    referenceColumns[index].Header,
                    4));
        }

        builder.Append("</row>");

        for (var row = 2;
             row <= maxReferenceRows;
             row++)
        {
            builder.Append("<row r=\"");
            builder.Append(row);
            builder.Append("\">");

            for (var index = 0;
                 index < referenceColumns.Count;
                 index++)
            {
                var values =
                    referenceColumns[index].Values;
                var valueIndex = row - 2;

                if (valueIndex < values.Count)
                {
                    builder.Append(
                        BuildInlineCell(
                            row,
                            firstReferenceColumn + index,
                            values[valueIndex],
                            5));
                }
            }

            builder.Append("</row>");
        }

        builder.Append("</sheetData>");
        builder.Append("<autoFilter ref=\"A1:");
        builder.Append(dataEndReference);
        builder.Append("\"/>");

        var validations = BuildDataValidations(
            dataColumns,
            referenceColumns,
            firstReferenceColumn,
            maxRow);

        builder.Append(validations);
        builder.Append("</worksheet>");

        return builder.ToString();
    }

    private static string BuildDataValidations(
        IReadOnlyList<EmployeeTemplateColumn> dataColumns,
        IReadOnlyList<ReferenceColumn> referenceColumns,
        int firstReferenceColumn,
        int maxRow)
    {
        var referenceMap = referenceColumns
            .Select((column, index) => new
            {
                column.Header,
                ColumnNumber =
                    firstReferenceColumn + index,
                LastRow = Math.Max(
                    2,
                    column.Values.Count + 1)
            })
            .ToDictionary(
                item => item.Header,
                item => item,
                StringComparer.OrdinalIgnoreCase);

        var mappings = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["CompanyName"] = "Ref Company Name",
            ["CompanyCode"] = "Ref Company Code",
            ["WorkLocationName"] =
                "Ref Work Location Name",
            ["WorkLocationCode"] =
                "Ref Work Location Code",
            ["DepartmentName"] =
                "Ref Department Name",
            ["DepartmentCode"] =
                "Ref Department Code",
            ["PositionName"] =
                "Ref Position Name",
            ["PositionCode"] =
                "Ref Position Code"
        };

        var items = new List<string>();

        for (var index = 0;
             index < dataColumns.Count;
             index++)
        {
            var column = dataColumns[index];

            if (!mappings.TryGetValue(
                    column.Name,
                    out var referenceHeader) ||
                !referenceMap.TryGetValue(
                    referenceHeader,
                    out var reference))
            {
                continue;
            }

            var targetColumn =
                GetColumnName(index + 1);
            var referenceColumn =
                GetColumnName(
                    reference.ColumnNumber);
            var formula =
                $"${referenceColumn}$2:" +
                $"${referenceColumn}$" +
                $"{reference.LastRow}";
            var sqref =
                $"{targetColumn}2:" +
                $"{targetColumn}{maxRow}";

            items.Add(
                "<dataValidation type=\"list\" allowBlank=\"1\" showErrorMessage=\"0\" showInputMessage=\"1\" " +
                $"sqref=\"{sqref}\" promptTitle=\"NEXORA reference\" prompt=\"Select an existing value or type a new value to create it automatically.\">" +
                $"<formula1>{formula}</formula1>" +
                "</dataValidation>");
        }

        if (items.Count == 0)
        {
            return string.Empty;
        }

        return
            $"<dataValidations count=\"{items.Count}\">" +
            string.Concat(items) +
            "</dataValidations>";
    }

    private static string BuildInlineCell(
        int row,
        int column,
        string value,
        int style)
    {
        return
            $"<c r=\"{GetCellReference(row, column)}\" " +
            $"s=\"{style}\" t=\"inlineStr\"><is><t>" +
            $"{Xml(value)}</t></is></c>";
    }

    private static string GetCellReference(
        int row,
        int column)
    {
        return GetColumnName(column) +
               row.ToString(
                   CultureInfo.InvariantCulture);
    }

    private static string GetColumnName(int column)
    {
        var result = string.Empty;
        var dividend = column;

        while (dividend > 0)
        {
            var modulo =
                (dividend - 1) % 26;
            result =
                Convert.ToChar(65 + modulo) +
                result;
            dividend =
                (dividend - modulo) / 26;
        }

        return result;
    }

    private static string Xml(string? value)
    {
        return System.Security.SecurityElement
            .Escape(value ?? string.Empty) ??
            string.Empty;
    }

    private static void AddZipEntry(
        ZipArchive archive,
        string entryName,
        string content)
    {
        var entry = archive.CreateEntry(
            entryName,
            CompressionLevel.Fastest);

        using var stream = entry.Open();
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false));

        writer.Write(content);
    }

    private async Task ExecuteSqlAsync(
        string sql,
        Action<DbCommand> configure)
    {
        var connection =
            _dbContext.Database.GetDbConnection();
        var shouldClose =
            connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command =
                connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction =
                _dbContext.Database.CurrentTransaction
                    ?.GetDbTransaction();
            configure(command);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            if (shouldClose &&
                _dbContext.Database.CurrentTransaction == null)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<int> ExecuteScalarIntAsync(
        string sql,
        Action<DbCommand> configure)
    {
        var connection =
            _dbContext.Database.GetDbConnection();
        var shouldClose =
            connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command =
                connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction =
                _dbContext.Database.CurrentTransaction
                    ?.GetDbTransaction();
            configure(command);
            var value =
                await command.ExecuteScalarAsync();

            return Convert.ToInt32(
                value,
                CultureInfo.InvariantCulture);
        }
        finally
        {
            if (shouldClose &&
                _dbContext.Database.CurrentTransaction == null)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<List<T>> QueryAsync<T>(
        string sql,
        Action<DbCommand> configure,
        Func<DbDataReader, T> map)
    {
        var result = new List<T>();
        var connection =
            _dbContext.Database.GetDbConnection();
        var shouldClose =
            connection.State != ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command =
                connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction =
                _dbContext.Database.CurrentTransaction
                    ?.GetDbTransaction();
            configure(command);

            await using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(map(reader));
            }
        }
        finally
        {
            if (shouldClose &&
                _dbContext.Database.CurrentTransaction == null)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }

    private static Company? ResolveExistingCompany(
        IReadOnlyList<Company> companies,
        string name,
        string code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var byCode = companies.FirstOrDefault(
                company => Same(company.Code, code));

            if (byCode != null)
            {
                return byCode;
            }
        }

        return companies.FirstOrDefault(
            company => Same(company.Name, name));
    }

    private static string SelectCode(
        string requestedCode,
        string prefix,
        HashSet<string> usedCodes)
    {
        var normalizedRequested =
            NormalizeCode(requestedCode);

        if (!string.IsNullOrWhiteSpace(
                normalizedRequested) &&
            !usedCodes.Contains(
                normalizedRequested))
        {
            return normalizedRequested;
        }

        return GenerateShortCode(
            prefix,
            usedCodes);
    }

    private static string GenerateShortCode(
        string prefix,
        HashSet<string> usedCodes)
    {
        for (var number = 1;
             number < 1000000;
             number++)
        {
            var candidate =
                $"{prefix}-{number:000}";

            if (!usedCodes.Contains(candidate))
            {
                return candidate;
            }
        }

        return
            $"{prefix}-" +
            Guid.NewGuid()
                .ToString("N")
                .Substring(0, 8)
                .ToUpperInvariant();
    }

    private static void ApplyOptional(
        string value,
        bool created,
        Action<string?> apply)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            apply(value.Trim());
        }
        else if (created)
        {
            apply(null);
        }
    }

    private static void Require(
        string value,
        string field,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} is required.");
        }
    }

    private static void ValidateLength(
        string value,
        int maxLength,
        string field,
        ICollection<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            value.Trim().Length > maxLength)
        {
            errors.Add(
                $"{field} exceeds the maximum length " +
                $"of {maxLength} characters.");
        }
    }

    private static string GetValue(
        IReadOnlyDictionary<string, string> values,
        params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var normalizedAlias =
                NormalizeHeader(alias);

            foreach (var pair in values)
            {
                if (NormalizeHeader(pair.Key) ==
                    normalizedAlias)
                {
                    return pair.Value?.Trim() ??
                           string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private static bool TryParseBoolean(
        string value,
        out bool result)
    {
        var normalized =
            NormalizeKey(value);

        if (normalized is
            "1" or
            "yes" or
            "y" or
            "true" or
            "active" or
            "فعال" or
            "نعم")
        {
            result = true;
            return true;
        }

        if (normalized is
            "0" or
            "no" or
            "n" or
            "false" or
            "inactive" or
            "غير فعال" or
            "لا")
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryParseDate(
        string value,
        out DateOnly date)
    {
        date = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var formats = new[]
        {
            "yyyy-MM-dd",
            "yyyy/MM/dd",
            "MM/dd/yyyy",
            "M/d/yyyy",
            "dd/MM/yyyy",
            "d/M/yyyy"
        };

        if (DateTime.TryParseExact(
                value.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exact))
        {
            date = DateOnly.FromDateTime(exact);
            return true;
        }

        if (double.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var numericDate) &&
            numericDate > 20000 &&
            numericDate < 90000)
        {
            date = DateOnly.FromDateTime(
                DateTime.FromOADate(
                    numericDate));
            return true;
        }

        return false;
    }

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        var dot = value.LastIndexOf('.');

        return at > 0 &&
               dot > at + 1 &&
               dot < value.Length - 1;
    }

    private static bool TryResolveDynamicHeader(
        string header,
        IReadOnlyList<DynamicFieldDefinition> definitions,
        out DynamicFieldDefinition definition)
    {
        definition = DynamicFieldDefinition.Empty;
        var value = header
            .Replace('\u00A0', ' ')
            .Trim();

        if (!value.StartsWith(
                "Custom:",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        value = value
            .Substring("Custom:".Length)
            .Trim();

        var bracketKey =
            ExtractBracketKey(value);

        if (!string.IsNullOrWhiteSpace(bracketKey))
        {
            var byKey = definitions.FirstOrDefault(item =>
                Same(item.FieldKey, bracketKey));

            if (byKey != null)
            {
                definition = byKey;
                return true;
            }

            value = RemoveBracketKey(value);
        }

        var byLabel = definitions.FirstOrDefault(item =>
            Same(item.FieldLabel, value));

        if (byLabel != null)
        {
            definition = byLabel;
            return true;
        }

        var byFieldKey = definitions.FirstOrDefault(item =>
            Same(item.FieldKey, value));

        if (byFieldKey != null)
        {
            definition = byFieldKey;
            return true;
        }

        return false;
    }

    private static string BuildDynamicHeader(
        DynamicFieldDefinition field,
        IEnumerable<string> usedHeaders)
    {
        var label = string.IsNullOrWhiteSpace(
                field.FieldLabel)
            ? field.FieldKey
            : field.FieldLabel.Trim();
        var header = $"Custom: {label}";
        var used = usedHeaders.ToHashSet(
            StringComparer.OrdinalIgnoreCase);

        if (used.Contains(header))
        {
            header =
                $"Custom: {label} [{field.FieldKey}]";
        }

        return header;
    }

    private static string ExtractBracketKey(
        string value)
    {
        var start = value.LastIndexOf('[');
        var end = value.LastIndexOf(']');

        if (start >= 0 && end > start)
        {
            return value
                .Substring(
                    start + 1,
                    end - start - 1)
                .Trim();
        }

        return string.Empty;
    }

    private static string RemoveBracketKey(
        string value)
    {
        var start = value.LastIndexOf('[');
        var end = value.LastIndexOf(']');

        if (start >= 0 && end > start)
        {
            return value
                .Remove(
                    start,
                    end - start + 1)
                .Trim();
        }

        return value.Trim();
    }

    private static string NormalizeHeader(
        string? value)
    {
        return new string(
            (value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

    private static string NormalizeKey(
        string? value)
    {
        var cleaned =
            (value ?? string.Empty)
                .Replace('\u00A0', ' ')
                .Trim();

        while (cleaned.Contains("  "))
        {
            cleaned = cleaned.Replace("  ", " ");
        }

        return cleaned.ToLowerInvariant();
    }

    private static string NormalizeIdentifier(
        string? value)
    {
        var cleaned =
            (value ?? string.Empty)
                .Replace('\u00A0', ' ')
                .Trim();

        while (cleaned.Contains("  "))
        {
            cleaned =
                cleaned.Replace("  ", " ");
        }

        return cleaned;
    }

    private static string NormalizeCode(
        string? value)
    {
        var cleaned =
            (value ?? string.Empty)
                .Replace('\u00A0', ' ')
                .Trim();

        while (cleaned.Contains(" -"))
        {
            cleaned =
                cleaned.Replace(" -", "-");
        }

        while (cleaned.Contains("- "))
        {
            cleaned =
                cleaned.Replace("- ", "-");
        }

        while (cleaned.Contains("  "))
        {
            cleaned =
                cleaned.Replace("  ", " ");
        }

        return cleaned.ToUpperInvariant();
    }

    private static bool Same(
        string? left,
        string? right)
    {
        return string.Equals(
            NormalizeKey(left),
            NormalizeKey(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string? NullIfBlank(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        object? value)
    {
        var parameter =
            command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value =
            value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static int GetInt32(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        return reader.IsDBNull(ordinal)
            ? 0
            : Convert.ToInt32(
                reader.GetValue(ordinal),
                CultureInfo.InvariantCulture);
    }

    private static int? GetNullableInt32(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt32(
                reader.GetValue(ordinal),
                CultureInfo.InvariantCulture);
    }

    private static string GetString(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        return reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(
                  reader.GetValue(ordinal),
                  CultureInfo.InvariantCulture) ??
              string.Empty;
    }

    private static bool GetBoolean(
        DbDataReader reader,
        string name)
    {
        var ordinal =
            reader.GetOrdinal(name);

        return !reader.IsDBNull(ordinal) &&
               Convert.ToBoolean(
                   reader.GetValue(ordinal),
                   CultureInfo.InvariantCulture);
    }

    private sealed class EmployeeBootstrapPlan
    {
        public EmployeeBootstrapPlan(
            List<EmployeeBootstrapRowPlan> rows)
        {
            Rows = rows;
        }

        public List<EmployeeBootstrapRowPlan> Rows { get; }
    }

    private sealed class EmployeeBootstrapRowPlan
    {
        public int RowNumber { get; set; }
        public Dictionary<string, string> Values { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public string EmployeeNo { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string CompanyCode { get; set; } = string.Empty;
        public string WorkLocationName { get; set; } = string.Empty;
        public string WorkLocationCode { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string DepartmentCode { get; set; } = string.Empty;
        public string PositionName { get; set; } = string.Empty;
        public string PositionCode { get; set; } = string.Empty;
        public DateOnly? HireDate { get; set; }
        public string NationalId { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateOnly? BirthDate { get; set; }
        public string Gender { get; set; } = string.Empty;
        public string MaritalStatus { get; set; } = string.Empty;
        public string Nationality { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string ContractType { get; set; } = string.Empty;
        public DateOnly? ContractEndDate { get; set; }
        public string EmploymentStatus { get; set; } = string.Empty;
        public bool? IsActive { get; set; }
        public string DirectManagerEmployeeNo { get; set; } = string.Empty;
        public string EmployeeAction { get; set; } = "Create";
        public CompanyReference? Company { get; set; }
        public BranchReference? Branch { get; set; }
        public DepartmentReference? Department { get; set; }
        public PositionReference? Position { get; set; }
        public List<string> Messages { get; } = new();
        public List<string> Errors { get; } = new();
        public bool CanImport => Errors.Count == 0;
    }

    private sealed class BootstrapSnapshot
    {
        public BootstrapSnapshot(
            List<CompanyReference> companies,
            List<BranchReference> branches,
            List<DepartmentReference> departments,
            List<PositionReference> positions,
            HashSet<string> employeeNumbers)
        {
            Companies = companies;
            Branches = branches;
            Departments = departments;
            Positions = positions;
            EmployeeNumbers = employeeNumbers;
        }

        public List<CompanyReference> Companies { get; }
        public List<BranchReference> Branches { get; }
        public List<DepartmentReference> Departments { get; }
        public List<PositionReference> Positions { get; }
        public HashSet<string> EmployeeNumbers { get; }
    }

    private sealed class CompanyReference
    {
        public CompanyReference(
            int id,
            string name,
            string code,
            bool isPlanned)
        {
            Id = id;
            Name = name;
            Code = code;
            IsPlanned = isPlanned;
            Key = id > 0
                ? $"id:{id}"
                : $"new-company:{NormalizeKey(name)}";
        }

        public int Id { get; }
        public string Name { get; }
        public string Code { get; }
        public bool IsPlanned { get; }
        public string Key { get; }
    }

    private sealed class BranchReference
    {
        public BranchReference(
            int id,
            string companyKey,
            string name,
            string code,
            bool isPlanned)
        {
            Id = id;
            CompanyKey = companyKey;
            Name = name;
            Code = code;
            IsPlanned = isPlanned;
            Key = id > 0
                ? $"id:{id}"
                : $"new-branch:{companyKey}:{NormalizeKey(name)}";
        }

        public int Id { get; }
        public string CompanyKey { get; }
        public string Name { get; }
        public string Code { get; }
        public bool IsPlanned { get; }
        public string Key { get; }
    }

    private sealed class DepartmentReference
    {
        public DepartmentReference(
            int id,
            string companyKey,
            string name,
            string code,
            bool isPlanned)
        {
            Id = id;
            CompanyKey = companyKey;
            Name = name;
            Code = code;
            IsPlanned = isPlanned;
            Key = id > 0
                ? $"id:{id}"
                : $"new-department:{companyKey}:{NormalizeKey(name)}";
        }

        public int Id { get; }
        public string CompanyKey { get; }
        public string Name { get; }
        public string Code { get; }
        public bool IsPlanned { get; }
        public string Key { get; }
    }

    private sealed class PositionReference
    {
        public PositionReference(
            int id,
            string companyKey,
            string departmentKey,
            string name,
            string code,
            bool isPlanned)
        {
            Id = id;
            CompanyKey = companyKey;
            DepartmentKey = departmentKey;
            Name = name;
            Code = code;
            IsPlanned = isPlanned;
            Key = id > 0
                ? $"id:{id}"
                : $"new-position:{companyKey}:{NormalizeKey(name)}";
        }

        public int Id { get; }
        public string CompanyKey { get; }
        public string DepartmentKey { get; }
        public string Name { get; }
        public string Code { get; }
        public bool IsPlanned { get; }
        public string Key { get; }
    }

    private sealed record PositionRow(
        int Id,
        int CompanyId,
        int? DepartmentId,
        string Name,
        string Code);

    private sealed record ParsedImportRow(
        int RowNumber,
        Dictionary<string, string> Values);

    private sealed class ParsedImportFile
    {
        public List<string> Headers { get; set; } = new();
        public List<ParsedImportRow> Rows { get; set; } = new();
    }

    private sealed record ResolvedEmployeeImportRow(
        EmployeeBootstrapRowPlan Plan,
        Employee Employee,
        bool Created);

    private sealed record DynamicFieldDefinition(
        string FieldKey,
        string FieldLabel,
        bool IsRequired,
        string SectionKey,
        int SortOrder)
    {
        public static DynamicFieldDefinition Empty { get; } =
            new(string.Empty, string.Empty, false, string.Empty, 0);
    }

    private sealed record TemplateReferenceRow(
        string Name,
        string Code,
        string Company);

    private sealed record TemplateReferenceData(
        List<TemplateReferenceRow> Companies,
        List<TemplateReferenceRow> Branches,
        List<TemplateReferenceRow> Departments,
        List<TemplateReferenceRow> Positions);

    private sealed record ReferenceColumn(
        string Header,
        List<string> Values);

    private sealed record EmployeeTemplateColumn(
        string Name,
        bool Required,
        EmployeeTemplateColumnKind Kind,
        double Width);

    private enum EmployeeTemplateColumnKind
    {
        Text,
        Date,
        Custom
    }

    private sealed class BootstrapStructureCounts
    {
        public int Companies { get; set; }
        public int Branches { get; set; }
        public int Departments { get; set; }
        public int Positions { get; set; }
    }
}
