using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.MasterDataImports.Services;
using SmartAttendance.Application.MasterDataImports.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;

using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
namespace SmartAttendance.Infrastructure.Services;

public class MasterDataImportService : IMasterDataImportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ApplicationDbContext _dbContext;

    private static readonly Dictionary<string, string[]> RequiredColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Companies"] = new[] { "Name" },
        ["Branches"] = new[] { "Name", "CompanyName" },
        ["Departments"] = new[] { "Name" },
        ["Employees"] = new[] { "EmployeeNo", "FullName", "DepartmentCode", "HireDate" },
        ["Devices"] = new[] { "Name", "IpAddress", "BranchCode" },
        ["Shifts"] = new[] { "Code", "Name", "StartTime", "EndTime" },
        ["EmployeeShifts"] = new[] { "EmployeeNo", "ShiftCode", "EffectiveFrom" },
        ["Holidays"] = new[] { "Name", "HolidayDate" },
        ["LeaveRequests"] = new[] { "EmployeeNo", "LeaveType", "FromDate", "ToDate" }
    };

    public MasterDataImportService(IUnitOfWork unitOfWork, ApplicationDbContext dbContext)
    {
        _unitOfWork = unitOfWork;
        _dbContext = dbContext;
    }

    public List<string> GetSupportedImportTypes()
    {
        return RequiredColumns.Keys.OrderBy(x => x).ToList();
    }

    public List<string> GetRequiredColumns(string importType)
    {
        if (!RequiredColumns.TryGetValue(importType, out var columns))
            return new List<string>();

        return columns.ToList();
    }

    public async Task<MasterDataImportPreviewViewModel> PreviewAsync(
        string filePath,
        string token,
        string originalFileName,
        string importType,
        int previewLimit = 500)
    {
        if (importType.Equals("Departments", StringComparison.OrdinalIgnoreCase))
            await EnsureIndependentDepartmentSchemaAsync();

        // NEXORA_FIX23A_PREVIEW_SCHEMA_CALL
        if (importType.Equals("Departments", StringComparison.OrdinalIgnoreCase))
            await EnsureIndependentDepartmentSchemaAsync();

        // NEXORA_FIX23A_IMPORT_SCHEMA_CALL
        var rows = await BuildPreviewRowsAsync(filePath, importType);

        return new MasterDataImportPreviewViewModel
        {
            Token = token,
            FileName = originalFileName,
            ImportType = importType,
            TotalRows = rows.Count,
            ReadyCount = rows.Count(x => x.CanImport),
            ErrorCount = rows.Count(x => x.Status == "Error"),
            CreateCount = rows.Count(x => x.Action == "Create" && x.CanImport),
            UpdateCount = rows.Count(x => x.Action == "Update" && x.CanImport),
            PreviewLimit = previewLimit,
            Rows = rows
                .OrderByDescending(x => x.Status == "Error")
                .ThenBy(x => x.RowNumber)
                .Take(previewLimit)
                .ToList()
        };
    }

    public async Task<MasterDataImportResultViewModel> ImportAsync(
        string filePath,
        string originalFileName,
        string importType)
    {
        var rows = await BuildPreviewRowsAsync(filePath, importType);
        var validRows = rows.Where(x => x.CanImport).ToList();

        var created = 0;
        var updated = 0;

        foreach (var row in validRows)
        {
            var wasCreated = await ImportRowAsync(importType, row.Values);

            if (wasCreated)
                created++;
            else
                updated++;
        }

        if (validRows.Any())
            await _unitOfWork.SaveChangesAsync();

        var skipped = rows.Count - validRows.Count;
        var errors = rows.Count(x => x.Status == "Error");

        return new MasterDataImportResultViewModel
        {
            CreatedCount = created,
            UpdatedCount = updated,
            SkippedCount = skipped,
            ErrorCount = errors,
            Message = $"Created {created}, Updated {updated}, Skipped {skipped}."
        };
    }

    private async Task<List<MasterDataImportPreviewRowViewModel>> BuildPreviewRowsAsync(
        string filePath,
        string importType)
    {
        if (!RequiredColumns.ContainsKey(importType))
            throw new InvalidOperationException("Unsupported import type.");

        var records = ReadFile(filePath);

        ValidateHeaders(records.Headers, importType);

        var rows = new List<MasterDataImportPreviewRowViewModel>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records.Rows)
        {
            var row = await ValidateRowAsync(importType, record);

            if (!string.IsNullOrWhiteSpace(row.Key))
            {
                var duplicateKey = $"{importType}|{row.Key}";

                if (!seenKeys.Add(duplicateKey))
                {
                    row.Status = "Error";
                    row.Message = "Duplicate key inside the same Excel file.";
                    row.CanImport = false;
                }
            }

            rows.Add(row);
        }

        return rows;
    }

            private void ValidateHeaders(List<string> headers, string importType)
    {
        var normalizedHeaders = headers
            .Select(NormalizeHeader)
            .ToHashSet();

        bool HasAny(params string[] columns)
        {
            return columns.Any(column => normalizedHeaders.Contains(NormalizeHeader(column)));
        }

        List<string> missingColumns;

        if (importType.Equals("Branches", StringComparison.OrdinalIgnoreCase))
        {
            missingColumns = new List<string>();

            if (!HasAny("Name"))
                missingColumns.Add("Name");

            if (!HasAny("CompanyName", "Company", "CompanyCode"))
                missingColumns.Add("CompanyName");
        }
        else if (importType.Equals("Departments", StringComparison.OrdinalIgnoreCase))
        {
            missingColumns = new List<string>();

            if (!HasAny("Name"))
                missingColumns.Add("Name");
        }
        else
        {
            missingColumns = RequiredColumns[importType]
                .Where(required => !normalizedHeaders.Contains(NormalizeHeader(required)))
                .ToList();
        }

        if (missingColumns.Any())
        {
            throw new InvalidOperationException(
                $"Missing required columns for {importType}: {string.Join(", ", missingColumns)}");
        }
    }
    private async Task<MasterDataImportPreviewRowViewModel> ValidateRowAsync(
        string importType,
        FileRow record)
    {
        return importType switch
        {
            "Companies" => await ValidateCompanyAsync(record),
            "Branches" => await ValidateBranchAsync(record),
            "Departments" => await ValidateDepartmentAsync(record),
            "Employees" => await ValidateEmployeeAsync(record),
            "Devices" => await ValidateDeviceAsync(record),
            "Shifts" => await ValidateShiftAsync(record),
            "EmployeeShifts" => await ValidateEmployeeShiftAsync(record),
            "Holidays" => await ValidateHolidayAsync(record),
            "LeaveRequests" => await ValidateLeaveRequestAsync(record),
            _ => BuildError(record, "-", "Unsupported import type.")
        };
    }

    private async Task<MasterDataImportPreviewRowViewModel> ValidateCompanyAsync(FileRow record)
    {
        var name = Get(record, "Name");

        if (IsBlank(name))
            return BuildError(record, name, "Name is required.");

        var companies = await _unitOfWork.Companies.GetAllAsync();
        var exists = companies.Any(x => Same(x.Name, name));

        return BuildReady(record, name, exists ? "Update" : "Create", exists ? "Company will be updated." : "Company will be created.");
    }
        private async Task<MasterDataImportPreviewRowViewModel> ValidateBranchAsync(FileRow record)
    {
        var name = Get(record, "Name");
        var companyKey = GetAny(record, "CompanyName", "Company", "CompanyCode");

        if (IsBlank(name) || IsBlank(companyKey))
            return BuildError(record, name, "Name and CompanyName are required.");

        var companies = await _unitOfWork.Companies.GetAllAsync();
        var company = companies.FirstOrDefault(x => Same(x.Name, companyKey) || Same(x.Code, companyKey));

        if (company == null)
            return BuildError(record, name, $"Company was not found: {companyKey}");

        var branches = await _unitOfWork.Branches.GetAllAsync();
        var exists = branches.Any(x => x.CompanyId == company.Id && Same(x.Name, name));

        var key = $"{company.Name} / {name}";
        return BuildReady(record, key, exists ? "Update" : "Create", exists ? "Branch will be updated." : "Branch will be created.");
    }
            private async Task<MasterDataImportPreviewRowViewModel> ValidateDepartmentAsync(FileRow record)
    {
        var name = Get(record, "Name");

        if (IsBlank(name))
            return BuildError(record, name, "Name is required.");

        var departments = await _unitOfWork.Departments.GetAllAsync();
        var exists = departments.Any(x => Same(x.Name, name));

        return BuildReady(record, name, exists ? "Update" : "Create", exists ? "Department will be updated." : "Department will be created.");
    }
    private async Task<MasterDataImportPreviewRowViewModel> ValidateEmployeeAsync(FileRow record)
    {
        var employeeNo = Get(record, "EmployeeNo");
        var fullName = Get(record, "FullName");
        var departmentCode = Get(record, "DepartmentCode");
        var hireDateText = Get(record, "HireDate");

        if (IsBlank(employeeNo) || IsBlank(fullName) || IsBlank(departmentCode) || IsBlank(hireDateText))
            return BuildError(record, employeeNo, "EmployeeNo, FullName, DepartmentCode and HireDate are required.");

        var departments = await _unitOfWork.Departments.GetAllAsync();
        if (!departments.Any(x => Same(x.Code, departmentCode)))
            return BuildError(record, employeeNo, $"DepartmentCode not found: {departmentCode}");

        if (!TryParseDate(hireDateText, out _))
            return BuildError(record, employeeNo, $"Invalid HireDate: {hireDateText}");

        var birthDateText = Get(record, "BirthDate");
        if (!IsBlank(birthDateText) && !TryParseDate(birthDateText, out _))
            return BuildError(record, employeeNo, $"Invalid BirthDate: {birthDateText}");

        var employees = await _unitOfWork.Employees.GetAllAsync();
        var exists = employees.Any(x => Same(x.EmployeeNo, employeeNo));

        return BuildReady(record, employeeNo, exists ? "Update" : "Create", exists ? "Employee will be updated." : "Employee will be created.");
    }

    private async Task<MasterDataImportPreviewRowViewModel> ValidateDeviceAsync(FileRow record)
    {
        var name = Get(record, "Name");
        var ipAddress = Get(record, "IpAddress");
        var branchCode = Get(record, "BranchCode");

        if (IsBlank(name) || IsBlank(ipAddress) || IsBlank(branchCode))
            return BuildError(record, name, "Name, IpAddress and BranchCode are required.");

        var branches = await _unitOfWork.Branches.GetAllAsync();
        if (!branches.Any(x => Same(x.Code, branchCode)))
            return BuildError(record, name, $"BranchCode not found: {branchCode}");

        var portText = Get(record, "Port");
        if (!IsBlank(portText) && !int.TryParse(portText, out _))
            return BuildError(record, name, $"Invalid Port: {portText}");

        var devices = await _unitOfWork.Devices.GetAllAsync();
        var serial = Get(record, "SerialNumber");
        var exists = !IsBlank(serial)
            ? devices.Any(x => Same(x.SerialNumber, serial))
            : devices.Any(x => Same(x.Name, name) && Same(x.IpAddress, ipAddress));

        return BuildReady(record, !IsBlank(serial) ? serial : name, exists ? "Update" : "Create", exists ? "Device will be updated." : "Device will be created.");
    }

    private async Task<MasterDataImportPreviewRowViewModel> ValidateShiftAsync(FileRow record)
    {
        var code = Get(record, "Code");
        var name = Get(record, "Name");
        var startTime = Get(record, "StartTime");
        var endTime = Get(record, "EndTime");

        if (IsBlank(code) || IsBlank(name) || IsBlank(startTime) || IsBlank(endTime))
            return BuildError(record, code, "Code, Name, StartTime and EndTime are required.");

        if (!TryParseTime(startTime, out _))
            return BuildError(record, code, $"Invalid StartTime: {startTime}");

        if (!TryParseTime(endTime, out _))
            return BuildError(record, code, $"Invalid EndTime: {endTime}");

        var workingHoursText = Get(record, "WorkingHours");
        if (!IsBlank(workingHoursText) && !decimal.TryParse(workingHoursText, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            return BuildError(record, code, $"Invalid WorkingHours: {workingHoursText}");

        var shifts = await _unitOfWork.Shifts.GetAllAsync();
        var exists = shifts.Any(x => Same(x.Code, code));

        return BuildReady(record, code, exists ? "Update" : "Create", exists ? "Shift will be updated." : "Shift will be created.");
    }

    private async Task<MasterDataImportPreviewRowViewModel> ValidateEmployeeShiftAsync(FileRow record)
    {
        var employeeNo = Get(record, "EmployeeNo");
        var shiftCode = Get(record, "ShiftCode");
        var effectiveFromText = Get(record, "EffectiveFrom");

        if (IsBlank(employeeNo) || IsBlank(shiftCode) || IsBlank(effectiveFromText))
            return BuildError(record, employeeNo, "EmployeeNo, ShiftCode and EffectiveFrom are required.");

        var employees = await _unitOfWork.Employees.GetAllAsync();
        if (!employees.Any(x => Same(x.EmployeeNo, employeeNo)))
            return BuildError(record, employeeNo, $"EmployeeNo not found: {employeeNo}");

        var shifts = await _unitOfWork.Shifts.GetAllAsync();
        if (!shifts.Any(x => Same(x.Code, shiftCode)))
            return BuildError(record, employeeNo, $"ShiftCode not found: {shiftCode}");

        if (!TryParseDate(effectiveFromText, out _))
            return BuildError(record, employeeNo, $"Invalid EffectiveFrom: {effectiveFromText}");

        var effectiveToText = Get(record, "EffectiveTo");
        if (!IsBlank(effectiveToText) && !TryParseDate(effectiveToText, out _))
            return BuildError(record, employeeNo, $"Invalid EffectiveTo: {effectiveToText}");

        var weeklyOffDays = Get(record, "WeeklyOffDays");
        if (!IsBlank(weeklyOffDays) && !IsValidWeeklyOffDays(weeklyOffDays))
            return BuildError(record, employeeNo, $"Invalid WeeklyOffDays: {weeklyOffDays}. Use Friday or Friday,Saturday.");

        return BuildReady(record, $"{employeeNo}-{shiftCode}-{effectiveFromText}", "Create", "Employee shift assignment will be created.");
    }

    private Task<MasterDataImportPreviewRowViewModel> ValidateHolidayAsync(FileRow record)
    {
        var name = Get(record, "Name");
        var holidayDateText = Get(record, "HolidayDate");

        if (IsBlank(name) || IsBlank(holidayDateText))
            return Task.FromResult(BuildError(record, name, "Name and HolidayDate are required."));

        if (!TryParseDate(holidayDateText, out _))
            return Task.FromResult(BuildError(record, name, $"Invalid HolidayDate: {holidayDateText}"));

        return Task.FromResult(BuildReady(record, $"{name}-{holidayDateText}", "Create", "Holiday will be created."));
    }

    private async Task<MasterDataImportPreviewRowViewModel> ValidateLeaveRequestAsync(FileRow record)
    {
        var employeeNo = Get(record, "EmployeeNo");
        var leaveTypeText = Get(record, "LeaveType");
        var fromDateText = Get(record, "FromDate");
        var toDateText = Get(record, "ToDate");

        if (IsBlank(employeeNo) || IsBlank(leaveTypeText) || IsBlank(fromDateText) || IsBlank(toDateText))
            return BuildError(record, employeeNo, "EmployeeNo, LeaveType, FromDate and ToDate are required.");

        var employees = await _unitOfWork.Employees.GetAllAsync();
        if (!employees.Any(x => Same(x.EmployeeNo, employeeNo)))
            return BuildError(record, employeeNo, $"EmployeeNo not found: {employeeNo}");

        if (!Enum.TryParse<LeaveType>(leaveTypeText, true, out _))
            return BuildError(record, employeeNo, $"Invalid LeaveType: {leaveTypeText}");

        if (!TryParseDate(fromDateText, out var fromDate))
            return BuildError(record, employeeNo, $"Invalid FromDate: {fromDateText}");

        if (!TryParseDate(toDateText, out var toDate))
            return BuildError(record, employeeNo, $"Invalid ToDate: {toDateText}");

        if (toDate < fromDate)
            return BuildError(record, employeeNo, "ToDate cannot be before FromDate.");

        var statusText = Get(record, "Status");
        if (!IsBlank(statusText) && !Enum.TryParse<LeaveStatus>(statusText, true, out _))
            return BuildError(record, employeeNo, $"Invalid Status: {statusText}");

        return BuildReady(record, $"{employeeNo}-{fromDateText}-{toDateText}", "Create", "Leave request will be created.");
    }

    private async Task<bool> ImportRowAsync(string importType, Dictionary<string, string> values)
    {
        return importType switch
        {
            "Companies" => await ImportCompanyAsync(values),
            "Branches" => await ImportBranchAsync(values),
            "Departments" => await ImportDepartmentAsync(values),
            "Employees" => await ImportEmployeeAsync(values),
            "Devices" => await ImportDeviceAsync(values),
            "Shifts" => await ImportShiftAsync(values),
            "EmployeeShifts" => await ImportEmployeeShiftAsync(values),
            "Holidays" => await ImportHolidayAsync(values),
            "LeaveRequests" => await ImportLeaveRequestAsync(values),
            _ => false
        };
    }

    private async Task<bool> ImportCompanyAsync(Dictionary<string, string> values)
    {
        var name = Get(values, "Name").Trim();
        var companies = await _unitOfWork.Companies.GetAllAsync();
        var company = companies.FirstOrDefault(x => Same(x.Name, name));

        var created = company == null;

        company ??= new Company();

        if (created)
        {
            var seed = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            var code = $"COMP-{seed}";
            var counter = 1;

            while (companies.Any(x => Same(x.Code, code)))
            {
                code = $"COMP-{seed}-{counter}";
                counter++;
            }

            company.Code = NormalizeCodeText(code);
        }

        company.Name = name;
        company.Email = NullIfBlank(Get(values, "Email"));
        company.Phone = NullIfBlank(Get(values, "Phone"));
        company.IsActive = ParseBool(Get(values, "IsActive"), true);

        if (created)
            await _unitOfWork.Companies.AddAsync(company);
        else
            _unitOfWork.Companies.Update(company);

        return created;
    }
        private async Task<bool> ImportBranchAsync(Dictionary<string, string> values)
    {
        var name = Get(values, "Name").Trim();
        var companyKey = GetAny(values, "CompanyName", "Company", "CompanyCode");

        var companies = await _unitOfWork.Companies.GetAllAsync();
        var company = companies.First(x => Same(x.Name, companyKey) || Same(x.Code, companyKey));

        var branches = await _unitOfWork.Branches.GetAllAsync();
        var branch = branches.FirstOrDefault(x => x.CompanyId == company.Id && Same(x.Name, name));

        var created = branch == null;
        branch ??= new Branch();

        var incomingCode = Get(values, "Code");
        if (created)
        {
            branch.Code = IsBlank(incomingCode)
                ? GenerateUniqueImportCode("BR", branches.Select(x => x.Code))
                : NormalizeCodeText(incomingCode);

            if (branches.Any(x => Same(x.Code, branch.Code)))
                branch.Code = GenerateUniqueImportCode("BR", branches.Select(x => x.Code));
        }
        else if (!IsBlank(incomingCode))
        {
            var normalizedIncomingCode = NormalizeCodeText(incomingCode);
            branch.Code = branches.Any(x => x.Id != branch.Id && Same(x.Code, normalizedIncomingCode))
                ? branch.Code
                : normalizedIncomingCode;
        }
        else if (IsBlank(branch.Code))
        {
            branch.Code = GenerateUniqueImportCode("BR", branches.Select(x => x.Code));
        }

        branch.Name = name;
        branch.CompanyId = company.Id;
        branch.Address = NullIfBlank(Get(values, "Address"));
        branch.IsActive = ParseBool(Get(values, "IsActive"), true);

        if (created)
            await _unitOfWork.Branches.AddAsync(branch);
        else
            _unitOfWork.Branches.Update(branch);

        return created;
    }
            private async Task<bool> ImportDepartmentAsync(Dictionary<string, string> values)
    {
        var name = Get(values, "Name").Trim();

        var departments = await _unitOfWork.Departments.GetAllAsync();
        var department = departments.FirstOrDefault(x => Same(x.Name, name));

        var created = department == null;
        department ??= new Department();

        var incomingCode = Get(values, "Code");
        if (created)
        {
            department.Code = IsBlank(incomingCode)
                ? GenerateUniqueImportCode("DEP", departments.Select(x => x.Code))
                : NormalizeCodeText(incomingCode);

            if (departments.Any(x => Same(x.Code, department.Code)))
                department.Code = GenerateUniqueImportCode("DEP", departments.Select(x => x.Code));
        }
        else if (!IsBlank(incomingCode))
        {
            var normalizedIncomingCode = NormalizeCodeText(incomingCode);
            department.Code = departments.Any(x => x.Id != department.Id && Same(x.Code, normalizedIncomingCode))
                ? department.Code
                : normalizedIncomingCode;
        }
        else if (IsBlank(department.Code))
        {
            department.Code = GenerateUniqueImportCode("DEP", departments.Select(x => x.Code));
        }

        department.Name = name;
        department.BranchId = null;
        department.IsActive = ParseBool(Get(values, "IsActive"), true);

        if (created)
            await _unitOfWork.Departments.AddAsync(department);
        else
            _unitOfWork.Departments.Update(department);

        return created;
    }
    private async Task<bool> ImportEmployeeAsync(Dictionary<string, string> values)
    {
        var employeeNo = Get(values, "EmployeeNo");
        var departmentCode = Get(values, "DepartmentCode");

        var departments = await _unitOfWork.Departments.GetAllAsync();
        var department = departments.First(x => Same(x.Code, departmentCode));

        var employees = await _unitOfWork.Employees.GetAllAsync();
        var employee = employees.FirstOrDefault(x => Same(x.EmployeeNo, employeeNo));

        var created = employee == null;

        employee ??= new Employee();

        employee.EmployeeNo = NormalizeCodeText(employeeNo);
        employee.FullName = Get(values, "FullName").Trim();
        employee.DepartmentId = department.Id;
        employee.HireDate = ParseDate(Get(values, "HireDate"));
        employee.NationalId = NullIfBlank(Get(values, "NationalId"));
        employee.Phone = NullIfBlank(Get(values, "Phone"));
        employee.Email = NullIfBlank(Get(values, "Email"));
        employee.BirthDate = IsBlank(Get(values, "BirthDate")) ? null : ParseDate(Get(values, "BirthDate"));
        employee.IsActive = ParseBool(Get(values, "IsActive"), true);

        if (created)
            await _unitOfWork.Employees.AddAsync(employee);
        else
            _unitOfWork.Employees.Update(employee);

        return created;
    }

    private async Task<bool> ImportDeviceAsync(Dictionary<string, string> values)
    {
        var name = Get(values, "Name");
        var ipAddress = Get(values, "IpAddress");
        var serialNumber = Get(values, "SerialNumber");
        var branchCode = Get(values, "BranchCode");

        var branches = await _unitOfWork.Branches.GetAllAsync();
        var branch = branches.First(x => Same(x.Code, branchCode));

        var devices = await _unitOfWork.Devices.GetAllAsync();
        var device = !IsBlank(serialNumber)
            ? devices.FirstOrDefault(x => Same(x.SerialNumber, serialNumber))
            : devices.FirstOrDefault(x => Same(x.Name, name) && Same(x.IpAddress, ipAddress));

        var created = device == null;

        device ??= new Device();

        device.Name = name.Trim();
        device.IpAddress = ipAddress.Trim();
        device.BranchId = branch.Id;
        device.Port = IsBlank(Get(values, "Port")) ? 4370 : int.Parse(Get(values, "Port"));
        device.SerialNumber = serialNumber.Trim();
        device.Model = NullIfBlank(Get(values, "Model"));
        device.FirmwareVersion = NullIfBlank(Get(values, "FirmwareVersion"));
        device.IsActive = ParseBool(Get(values, "IsActive"), true);
        device.IsEnabled = ParseBool(Get(values, "IsEnabled"), true);

        if (created)
            await _unitOfWork.Devices.AddAsync(device);
        else
            _unitOfWork.Devices.Update(device);

        return created;
    }

    private async Task<bool> ImportShiftAsync(Dictionary<string, string> values)
    {
        var code = Get(values, "Code");

        var shifts = await _unitOfWork.Shifts.GetAllAsync();
        var shift = shifts.FirstOrDefault(x => Same(x.Code, code));

        var created = shift == null;

        shift ??= new Shift();

        var startTime = ParseTime(Get(values, "StartTime"));
        var endTime = ParseTime(Get(values, "EndTime"));

        shift.Code = NormalizeCodeText(code);
        shift.Name = Get(values, "Name").Trim();
        shift.StartTime = startTime;
        shift.EndTime = endTime;
        shift.IsNightShift = ParseBool(Get(values, "IsNightShift"), endTime <= startTime);
        shift.WorkingHours = IsBlank(Get(values, "WorkingHours"))
            ? CalculateWorkingHours(startTime, endTime)
            : decimal.Parse(Get(values, "WorkingHours"), CultureInfo.InvariantCulture);
        shift.GraceInMinutes = IsBlank(Get(values, "GraceInMinutes")) ? 0 : int.Parse(Get(values, "GraceInMinutes"));
        shift.GraceOutMinutes = IsBlank(Get(values, "GraceOutMinutes")) ? 0 : int.Parse(Get(values, "GraceOutMinutes"));
        shift.IsActive = ParseBool(Get(values, "IsActive"), true);

        if (created)
            await _unitOfWork.Shifts.AddAsync(shift);
        else
            _unitOfWork.Shifts.Update(shift);

        return created;
    }

    private async Task<bool> ImportEmployeeShiftAsync(Dictionary<string, string> values)
    {
        var employeeNo = Get(values, "EmployeeNo");
        var shiftCode = Get(values, "ShiftCode");

        var employees = await _unitOfWork.Employees.GetAllAsync();
        var employee = employees.First(x => Same(x.EmployeeNo, employeeNo));

        var shifts = await _unitOfWork.Shifts.GetAllAsync();
        var shift = shifts.First(x => Same(x.Code, shiftCode));

        var assignment = new EmployeeShift
        {
            EmployeeId = employee.Id,
            ShiftId = shift.Id,
            EffectiveFrom = ParseDate(Get(values, "EffectiveFrom")),
            EffectiveTo = IsBlank(Get(values, "EffectiveTo")) ? null : ParseDate(Get(values, "EffectiveTo")),
            IsCurrent = ParseBool(Get(values, "IsCurrent"), true),
            WeeklyOffDays = NormalizeWeeklyOffDays(Get(values, "WeeklyOffDays"))
        };

        await _unitOfWork.EmployeeShifts.AddAsync(assignment);

        return true;
    }

    private async Task<bool> ImportHolidayAsync(Dictionary<string, string> values)
    {
        var holiday = new Holiday
        {
            Name = Get(values, "Name").Trim(),
            HolidayDate = ParseDate(Get(values, "HolidayDate")),
            IsRecurring = ParseBool(Get(values, "IsRecurring"), false),
            Description = NullIfBlank(Get(values, "Description"))
        };

        await _unitOfWork.Holidays.AddAsync(holiday);

        return true;
    }

    private async Task<bool> ImportLeaveRequestAsync(Dictionary<string, string> values)
    {
        var employeeNo = Get(values, "EmployeeNo");

        var employees = await _unitOfWork.Employees.GetAllAsync();
        var employee = employees.First(x => Same(x.EmployeeNo, employeeNo));

        var statusText = Get(values, "Status");
        var status = IsBlank(statusText)
            ? LeaveStatus.Approved
            : Enum.Parse<LeaveStatus>(statusText, true);

        var leaveRequest = new LeaveRequest
        {
            EmployeeId = employee.Id,
            LeaveType = Enum.Parse<LeaveType>(Get(values, "LeaveType"), true),
            Status = status,
            FromDate = ParseDate(Get(values, "FromDate")),
            ToDate = ParseDate(Get(values, "ToDate")),
            Reason = NullIfBlank(Get(values, "Reason"))
        };

        await _unitOfWork.LeaveRequests.AddAsync(leaveRequest);

        return true;
    }

    private static MasterDataImportPreviewRowViewModel BuildReady(
        FileRow record,
        string key,
        string action,
        string message)
    {
        return new MasterDataImportPreviewRowViewModel
        {
            RowNumber = record.RowNumber,
            Key = key,
            Action = action,
            Status = "Ready",
            Message = message,
            CanImport = true,
            Values = record.Values
        };
    }

    private static MasterDataImportPreviewRowViewModel BuildError(
        FileRow record,
        string key,
        string message)
    {
        return new MasterDataImportPreviewRowViewModel
        {
            RowNumber = record.RowNumber,
            Key = key,
            Action = "-",
            Status = "Error",
            Message = message,
            CanImport = false,
            Values = record.Values
        };
    }

    private static string Get(FileRow row, string columnName)
    {
        return Get(row.Values, columnName);
    }

    private static string Get(Dictionary<string, string> values, string columnName)
    {
        foreach (var key in values.Keys)
        {
            if (NormalizeHeader(key) == NormalizeHeader(columnName))
                return values[key]?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }


    // NEXORA_FIX22A_GETANY_HELPER
    private static string GetAny(FileRow row, params string[] columnNames)
    {
        return GetAny(row.Values, columnNames);
    }

    private static string GetAny(Dictionary<string, string> values, params string[] columnNames)
    {
        foreach (var columnName in columnNames)
        {
            var value = Get(values, columnName);
            if (!IsBlank(value))
                return value;
        }

        return string.Empty;
    }
    private static bool IsValidWeeklyOffDays(string? value)
    {
        if (IsBlank(value))
            return true;

        return value!
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(day => IsKnownDayName(day));
    }

    private static string? NormalizeWeeklyOffDays(string? value)
    {
        if (IsBlank(value))
            return null;

        return string.Join(",",
            value!
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeDayName)
                .Where(day => !string.IsNullOrWhiteSpace(day))
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsKnownDayName(string day)
    {
        return NormalizeDayName(day) is
            "Sunday" or "Monday" or "Tuesday" or "Wednesday" or "Thursday" or "Friday" or "Saturday";
    }

    private static string NormalizeDayName(string day)
    {
        return day.Trim().ToLowerInvariant() switch
        {
            "sun" or "sunday" or "الاحد" or "الأحد" => "Sunday",
            "mon" or "monday" or "الاثنين" or "الإثنين" => "Monday",
            "tue" or "tuesday" or "الثلاثاء" => "Tuesday",
            "wed" or "wednesday" or "الاربعاء" or "الأربعاء" => "Wednesday",
            "thu" or "thursday" or "الخميس" => "Thursday",
            "fri" or "friday" or "الجمعة" => "Friday",
            "sat" or "saturday" or "السبت" => "Saturday",
            _ => day.Trim()
        };
    }

    private static bool Same(string? left, string? right)
    {
        return string.Equals(
            NormalizeCodeText(left),
            NormalizeCodeText(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCodeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var cleaned = value
            .Replace('\u00A0', ' ')
            .Trim();

        while (cleaned.Contains(" -"))
            cleaned = cleaned.Replace(" -", "-");

        while (cleaned.Contains("- "))
            cleaned = cleaned.Replace("- ", "-");

        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");

        return cleaned.Trim();
    }


    // NEXORA_FIX22A_UNIQUE_IMPORT_CODE
    private static string GenerateUniqueImportCode(string prefix, IEnumerable<string> existingCodes)
    {
        var existing = existingCodes
            .Select(NormalizeCodeText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seed = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var code = $"{prefix}-{seed}";
        var counter = 1;

        while (existing.Contains(code))
        {
            code = $"{prefix}-{seed}-{counter}";
            counter++;
        }

        return code;
    }
    private static bool IsBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value);
    }

    private static string? NullIfBlank(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ParseBool(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        value = value.Trim();

        if (bool.TryParse(value, out var result))
            return result;

        return value.ToLowerInvariant() switch
        {
            "1" => true,
            "yes" => true,
            "y" => true,
            "active" => true,
            "true" => true,
            "0" => false,
            "no" => false,
            "n" => false,
            "inactive" => false,
            "false" => false,
            _ => defaultValue
        };
    }

    private static DateOnly ParseDate(string value)
    {
        TryParseDate(value, out var date);
        return date;
    }

    private static bool TryParseDate(string value, out DateOnly date)
    {
        date = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

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
                out var exactDate))
        {
            date = DateOnly.FromDateTime(exactDate);
            return true;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            date = DateOnly.FromDateTime(parsedDate);
            return true;
        }

        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var numericDate) &&
            numericDate > 20000 && numericDate < 90000)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(numericDate));
            return true;
        }

        return false;
    }

    private static TimeOnly ParseTime(string value)
    {
        TryParseTime(value, out var time);
        return time;
    }

    private static bool TryParseTime(string value, out TimeOnly time)
    {
        time = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var formats = new[]
        {
            "HH:mm",
            "H:mm",
            "HH:mm:ss",
            "H:mm:ss",
            "h:mm tt",
            "hh:mm tt"
        };

        if (TimeOnly.TryParseExact(
                value.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out time))
        {
            return true;
        }

        if (TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            return true;

        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var numericTime) &&
            numericTime >= 0 && numericTime < 1)
        {
            time = TimeOnly.FromTimeSpan(TimeSpan.FromDays(numericTime));
            return true;
        }

        return false;
    }

    private static decimal CalculateWorkingHours(TimeOnly start, TimeOnly end)
    {
        var startDate = DateTime.Today.Add(start.ToTimeSpan());
        var endDate = DateTime.Today.Add(end.ToTimeSpan());

        if (end <= start)
            endDate = endDate.AddDays(1);

        return Math.Round((decimal)(endDate - startDate).TotalHours, 2);
    }

    private static FileReadResult ReadFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".xlsx" => ReadXlsx(filePath),
            ".csv" => ReadCsv(filePath),
            _ => throw new InvalidOperationException("Unsupported file type. Please upload .xlsx or .csv file.")
        };
    }

    private static FileReadResult ReadXlsx(string filePath)
    {
        var result = new FileReadResult();

        using var archive = ZipFile.OpenRead(filePath);

        var sharedStrings = ReadSharedStrings(archive);
        var worksheetPath = GetFirstWorksheetPath(archive);

        var worksheetEntry = archive.GetEntry(worksheetPath)
            ?? throw new InvalidOperationException("Worksheet not found inside Excel file.");

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var stream = worksheetEntry.Open();
        var document = XDocument.Load(stream);

        var rows = document
            .Descendants(ns + "sheetData")
            .Elements(ns + "row")
            .ToList();

        if (!rows.Any())
            return result;

        var headerCells = ReadRowCells(rows.First(), sharedStrings, ns);
        result.Headers = headerCells
            .OrderBy(x => x.Key)
            .Select(x => x.Value)
            .ToList();

        foreach (var row in rows.Skip(1))
        {
            var rowNumber = int.TryParse(row.Attribute("r")?.Value, out var rn)
                ? rn
                : result.Rows.Count + 2;

            var cells = ReadRowCells(row, sharedStrings, ns);

            if (!cells.Any())
                continue;

            var fileRow = new FileRow
            {
                RowNumber = rowNumber
            };

            for (var i = 0; i < result.Headers.Count; i++)
            {
                fileRow.Values[result.Headers[i]] = cells.TryGetValue(i, out var value)
                    ? value
                    : string.Empty;
            }

            if (fileRow.Values.Values.All(string.IsNullOrWhiteSpace))
                continue;

            result.Rows.Add(fileRow);
        }

        return result;
    }

    private static FileReadResult ReadCsv(string filePath)
    {
        var result = new FileReadResult();

        var lines = File.ReadAllLines(filePath, Encoding.UTF8);

        if (lines.Length == 0)
            return result;

        result.Headers = SplitCsvLine(lines[0]);

        for (var i = 1; i < lines.Length; i++)
        {
            var values = SplitCsvLine(lines[i]);

            var fileRow = new FileRow
            {
                RowNumber = i + 1
            };

            for (var h = 0; h < result.Headers.Count; h++)
            {
                fileRow.Values[result.Headers[h]] = h < values.Count
                    ? values[h]
                    : string.Empty;
            }

            if (fileRow.Values.Values.All(string.IsNullOrWhiteSpace))
                continue;

            result.Rows.Add(fileRow);
        }

        return result;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var sharedStrings = new List<string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");

        if (entry == null)
            return sharedStrings;

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        foreach (var si in document.Descendants(ns + "si"))
        {
            var text = string.Concat(si.Descendants(ns + "t").Select(x => x.Value));
            sharedStrings.Add(text);
        }

        return sharedStrings;
    }

    private static string GetFirstWorksheetPath(ZipArchive archive)
    {
        XNamespace mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbookEntry = archive.GetEntry("xl/workbook.xml")
            ?? throw new InvalidOperationException("workbook.xml not found.");

        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels")
            ?? throw new InvalidOperationException("workbook relationships not found.");

        using var workbookStream = workbookEntry.Open();
        var workbookDocument = XDocument.Load(workbookStream);

        var firstSheet = workbookDocument
            .Descendants(mainNs + "sheet")
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No sheets found in workbook.");

        var relationshipId = firstSheet.Attribute(relNs + "id")?.Value;

        if (string.IsNullOrWhiteSpace(relationshipId))
            throw new InvalidOperationException("First worksheet relationship id not found.");

        using var relsStream = relsEntry.Open();
        var relsDocument = XDocument.Load(relsStream);

        var relationship = relsDocument
            .Descendants(packageRelNs + "Relationship")
            .FirstOrDefault(x => x.Attribute("Id")?.Value == relationshipId)
            ?? throw new InvalidOperationException("Worksheet relationship not found.");

        var target = relationship.Attribute("Target")?.Value
            ?? throw new InvalidOperationException("Worksheet target not found.");

        if (target.StartsWith("/"))
            return target.TrimStart('/');

        return "xl/" + target.TrimStart('/');
    }

    private static Dictionary<int, string> ReadRowCells(
        XElement row,
        IReadOnlyList<string> sharedStrings,
        XNamespace ns)
    {
        var values = new Dictionary<int, string>();

        foreach (var cell in row.Elements(ns + "c"))
        {
            var reference = cell.Attribute("r")?.Value ?? string.Empty;
            var columnIndex = GetColumnIndex(reference);

            if (columnIndex < 0)
                continue;

            var type = cell.Attribute("t")?.Value;
            var rawValue = cell.Element(ns + "v")?.Value ?? string.Empty;

            string value;

            if (type == "s" &&
                int.TryParse(rawValue, out var sharedStringIndex) &&
                sharedStringIndex >= 0 &&
                sharedStringIndex < sharedStrings.Count)
            {
                value = sharedStrings[sharedStringIndex];
            }
            else if (type == "inlineStr")
            {
                value = string.Concat(cell.Descendants(ns + "t").Select(x => x.Value));
            }
            else
            {
                value = rawValue;
            }

            values[columnIndex] = value;
        }

        return values;
    }

    private static int GetColumnIndex(string cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
            return -1;

        var letters = new string(cellReference.TakeWhile(char.IsLetter).ToArray());

        if (string.IsNullOrWhiteSpace(letters))
            return -1;

        var index = 0;

        foreach (var letter in letters.ToUpperInvariant())
        {
            index *= 26;
            index += letter - 'A' + 1;
        }

        return index - 1;
    }

    private static string NormalizeHeader(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var insideQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];

            if (character == '"')
            {
                if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (character == ',' && !insideQuotes)
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

    private class FileReadResult
    {
        public List<string> Headers { get; set; } = new();

        public List<FileRow> Rows { get; set; } = new();
    }

    private class FileRow
    {
        public int RowNumber { get; set; }

        public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    // NEXORA_FIX23A_DEPARTMENT_INDEPENDENT_SCHEMA
    private async Task EnsureIndependentDepartmentSchemaAsync()
    {
        await _dbContext.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('dbo.Departments', 'BranchId') IS NOT NULL
BEGIN
    DECLARE @dropFkSql NVARCHAR(MAX) = N'';

    SELECT @dropFkSql = @dropFkSql + N'ALTER TABLE dbo.Departments DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
    FROM sys.foreign_keys fk
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Departments')
      AND fk.referenced_object_id = OBJECT_ID(N'dbo.Branches');

    IF LEN(@dropFkSql) > 0
        EXEC sp_executesql @dropFkSql;

    DECLARE @dropIndexSql NVARCHAR(MAX) = N'';

    SELECT @dropIndexSql = @dropIndexSql + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.Departments;'
    FROM sys.indexes i
    INNER JOIN sys.index_columns ic
        ON i.object_id = ic.object_id
       AND i.index_id = ic.index_id
    INNER JOIN sys.columns c
        ON ic.object_id = c.object_id
       AND ic.column_id = c.column_id
    WHERE i.object_id = OBJECT_ID(N'dbo.Departments')
      AND i.is_primary_key = 0
      AND i.name IS NOT NULL
      AND c.name = N'BranchId';

    IF LEN(@dropIndexSql) > 0
        EXEC sp_executesql @dropIndexSql;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.Departments')
          AND name = N'BranchId'
          AND is_nullable = 0
    )
    BEGIN
        ALTER TABLE dbo.Departments ALTER COLUMN BranchId INT NULL;
    END
END;
");
    }
}
