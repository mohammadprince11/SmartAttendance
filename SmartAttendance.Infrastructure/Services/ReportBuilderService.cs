using SmartAttendance.Application.AttendanceProcessing.Services;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.ReportBuilder.Services;
using SmartAttendance.Application.ReportBuilder.ViewModels;

namespace SmartAttendance.Infrastructure.Services;

public class ReportBuilderService : IReportBuilderService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceProcessingService _attendanceProcessingService;

    public ReportBuilderService(
        IUnitOfWork unitOfWork,
        IAttendanceProcessingService attendanceProcessingService)
    {
        _unitOfWork = unitOfWork;
        _attendanceProcessingService = attendanceProcessingService;
    }

    public List<ReportBuilderColumnViewModel> GetColumns(string reportType)
    {
        return NormalizeReportType(reportType) switch
        {
            "Attendance" => GetAttendanceColumns(),
            _ => GetEmployeeColumns()
        };
    }

    public async Task<List<ReportBuilderDropdownItemViewModel>> GetBranchesAsync()
    {
        var branches = await _unitOfWork.Branches.GetAllAsync();

        return branches
            .OrderBy(x => x.Name)
            .Select(x => new ReportBuilderDropdownItemViewModel
            {
                Value = x.Id.ToString(),
                Text = $"{x.Code} - {x.Name}"
            })
            .ToList();
    }

    public async Task<List<ReportBuilderDropdownItemViewModel>> GetDepartmentsAsync(string? branchId)
    {
        var departments = (await _unitOfWork.Departments.GetAllAsync()).ToList();
        var branches = (await _unitOfWork.Branches.GetAllAsync()).ToList();

        if (int.TryParse(branchId, out var parsedBranchId))
            departments = departments.Where(x => x.BranchId == parsedBranchId).ToList();

        var branchLookup = branches.ToDictionary(x => x.Id, x => x.Name);

        return departments
            .OrderBy(x => x.BranchId.HasValue && branchLookup.TryGetValue(x.BranchId.Value, out var branchName) ? branchName : string.Empty)
            .ThenBy(x => x.Name)
            .Select(x => new ReportBuilderDropdownItemViewModel
            {
                Value = x.Id.ToString(),
                Text = x.Name
            })
            .ToList();
    }

    public async Task<List<ReportBuilderDropdownItemViewModel>> GetShiftsAsync()
    {
        var shifts = await _unitOfWork.Shifts.GetAllAsync();

        return shifts
            .Where(x => x.IsActive)
            .OrderBy(x => x.StartTime)
            .Select(x => new ReportBuilderDropdownItemViewModel
            {
                Value = x.Id.ToString(),
                Text = $"{x.Code} - {x.Name} ({x.StartTime:HH:mm}-{x.EndTime:HH:mm})"
            })
            .ToList();
    }

    public async Task<ReportBuilderResultViewModel> BuildAsync(ReportBuilderRequestViewModel request)
    {
        request.ReportType = NormalizeReportType(request.ReportType);

        var availableColumns = GetColumns(request.ReportType);

        if (request.SelectedColumns == null || request.SelectedColumns.Count == 0)
            request.SelectedColumns = GetDefaultColumns(request.ReportType);

        var selectedKeys = request.SelectedColumns
            .Where(key => availableColumns.Any(col => col.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var column in availableColumns)
            column.IsSelected = selectedKeys.Contains(column.Key, StringComparer.OrdinalIgnoreCase);

        return request.ReportType == "Attendance"
            ? await BuildAttendanceResultAsync(request, availableColumns, selectedKeys)
            : await BuildEmployeeResultAsync(request, availableColumns, selectedKeys);
    }

    private async Task<ReportBuilderResultViewModel> BuildEmployeeResultAsync(
        ReportBuilderRequestViewModel request,
        List<ReportBuilderColumnViewModel> availableColumns,
        List<string> selectedKeys)
    {
        var employees = (await _unitOfWork.Employees.GetAllAsync()).ToList();
        var departments = (await _unitOfWork.Departments.GetAllAsync()).ToList();
        var branches = (await _unitOfWork.Branches.GetAllAsync()).ToList();

        var departmentLookup = departments.ToDictionary(x => x.Id);
        var branchLookup = branches.ToDictionary(x => x.Id);

        var query = employees.AsEnumerable();

        if (int.TryParse(request.DepartmentId, out var departmentId))
            query = query.Where(employee => employee.DepartmentId == departmentId);

        if (int.TryParse(request.BranchId, out var branchId))
        {
            var departmentIds = departments
                .Where(x => x.BranchId == branchId)
                .Select(x => x.Id)
                .ToHashSet();

            query = query.Where(employee => departmentIds.Contains(employee.DepartmentId));
        }

        if (!string.IsNullOrWhiteSpace(request.ActiveFilter))
        {
            if (request.ActiveFilter.Equals("Active", StringComparison.OrdinalIgnoreCase))
                query = query.Where(employee => employee.IsActive);
            else if (request.ActiveFilter.Equals("Inactive", StringComparison.OrdinalIgnoreCase))
                query = query.Where(employee => !employee.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            query = query.Where(employee =>
                employee.EmployeeNo.Contains(request.SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                employee.FullName.Contains(request.SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                (employee.NationalId != null && employee.NationalId.Contains(request.SearchTerm, StringComparison.OrdinalIgnoreCase)) ||
                (employee.Phone != null && employee.Phone.Contains(request.SearchTerm, StringComparison.OrdinalIgnoreCase)) ||
                (employee.Email != null && employee.Email.Contains(request.SearchTerm, StringComparison.OrdinalIgnoreCase)));
        }

        var materialized = query.OrderBy(x => x.EmployeeNo).ToList();
        var rows = new List<Dictionary<string, string>>();

        foreach (var employee in materialized)
        {
            departmentLookup.TryGetValue(employee.DepartmentId, out var department);

            string branchName = string.Empty;
            string branchCode = string.Empty;

            if (department?.BranchId != null && branchLookup.TryGetValue(department.BranchId.Value, out var branch))
            {
                branchName = branch.Name;
                branchCode = branch.Code;
            }

            var allValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EmployeeNo"] = employee.EmployeeNo,
                ["EmployeeName"] = employee.FullName,
                ["Department"] = department?.Name ?? string.Empty,
                ["Branch"] = branchName,
                ["BranchCode"] = branchCode,

                // Standard date format for all reports: yyyy-MM-dd
                ["HireDate"] = employee.HireDate.ToString("yyyy-MM-dd"),
                ["BirthDate"] = employee.BirthDate?.ToString("yyyy-MM-dd") ?? string.Empty,

                ["NationalId"] = employee.NationalId ?? string.Empty,
                ["Phone"] = employee.Phone ?? string.Empty,
                ["Email"] = employee.Email ?? string.Empty,
                ["IsActive"] = employee.IsActive ? "Yes" : "No"
            };

            rows.Add(FilterColumns(allValues, selectedKeys));
        }

        var summary = new Dictionary<string, string>
        {
            ["Total Employees"] = materialized.Count.ToString(),
            ["Active"] = materialized.Count(x => x.IsActive).ToString(),
            ["Inactive"] = materialized.Count(x => !x.IsActive).ToString(),
            ["Branches"] = materialized
                .Select(x =>
                {
                    if (departmentLookup.TryGetValue(x.DepartmentId, out var department) &&
                        department.BranchId.HasValue &&
                        branchLookup.TryGetValue(department.BranchId.Value, out var branch))
                        return branch.Id;

                    return 0;
                })
                .Where(x => x > 0)
                .Distinct()
                .Count()
                .ToString()
        };

        return new ReportBuilderResultViewModel
        {
            ReportType = "Employees",
            Columns = availableColumns.Where(x => selectedKeys.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).ToList(),
            Rows = rows,
            Summary = summary
        };
    }

    private async Task<ReportBuilderResultViewModel> BuildAttendanceResultAsync(
        ReportBuilderRequestViewModel request,
        List<ReportBuilderColumnViewModel> availableColumns,
        List<string> selectedKeys)
    {
        var fromDate = request.FromDate ?? DateOnly.FromDateTime(DateTime.Today);
        var toDate = request.ToDate ?? fromDate;

        var processedRows = (await _attendanceProcessingService.GetProcessedRecordsAsync(
            fromDate,
            toDate,
            request.SearchTerm)).ToList();

        var employees = (await _unitOfWork.Employees.GetAllAsync()).ToList();
        var departments = (await _unitOfWork.Departments.GetAllAsync()).ToList();
        var branches = (await _unitOfWork.Branches.GetAllAsync()).ToList();

        var employeeLookup = employees.ToDictionary(x => x.Id);
        var departmentLookup = departments.ToDictionary(x => x.Id);
        var branchLookup = branches.ToDictionary(x => x.Id);

        var query = processedRows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(request.StatusFilter))
            query = query.Where(x => x.CalculatedStatus.Equals(request.StatusFilter, StringComparison.OrdinalIgnoreCase));

        if (int.TryParse(request.ShiftId, out var shiftId))
        {
            var shifts = await _unitOfWork.Shifts.GetAllAsync();
            var shift = shifts.FirstOrDefault(x => x.Id == shiftId);

            if (shift != null)
                query = query.Where(x => x.ShiftCode.Equals(shift.Code, StringComparison.OrdinalIgnoreCase));
        }

        if (int.TryParse(request.DepartmentId, out var departmentId))
        {
            var employeeIds = employees
                .Where(x => x.DepartmentId == departmentId)
                .Select(x => x.Id)
                .ToHashSet();

            query = query.Where(x => employeeIds.Contains(x.EmployeeId));
        }

        if (int.TryParse(request.BranchId, out var branchId))
        {
            var departmentIds = departments
                .Where(x => x.BranchId == branchId)
                .Select(x => x.Id)
                .ToHashSet();

            var employeeIds = employees
                .Where(x => departmentIds.Contains(x.DepartmentId))
                .Select(x => x.Id)
                .ToHashSet();

            query = query.Where(x => employeeIds.Contains(x.EmployeeId));
        }

        var materialized = query
            .OrderByDescending(x => x.AttendanceDate)
            .ThenBy(x => x.EmployeeNo)
            .ToList();

        var rows = new List<Dictionary<string, string>>();

        foreach (var item in materialized)
        {
            string departmentName = string.Empty;
            string branchName = string.Empty;
            string branchCode = string.Empty;

            if (employeeLookup.TryGetValue(item.EmployeeId, out var employee) &&
                departmentLookup.TryGetValue(employee.DepartmentId, out var department))
            {
                departmentName = department.Name;

                if (department.BranchId.HasValue && branchLookup.TryGetValue(department.BranchId.Value, out var branch))
                {
                    branchName = branch.Name;
                    branchCode = branch.Code;
                }
            }

            var allValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Standard date format for all reports: yyyy-MM-dd
                ["Date"] = item.AttendanceDate.ToString("yyyy-MM-dd"),

                ["EmployeeNo"] = item.EmployeeNo,
                ["EmployeeName"] = item.EmployeeName,
                ["Branch"] = branchName,
                ["BranchCode"] = branchCode,
                ["Department"] = departmentName,
                ["Shift"] = string.IsNullOrWhiteSpace(item.ShiftName)
                    ? "-"
                    : $"{item.ShiftCode} - {item.ShiftName}",
                ["WeeklyOff"] = item.WeeklyOffDays ?? string.Empty,

                // DateTime keeps date part in yyyy-MM-dd too
                ["CheckIn"] = item.CheckIn?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
                ["CheckOut"] = item.CheckOut?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty,

                ["WorkHours"] = item.WorkingHours?.ToString("0.##") ?? string.Empty,
                ["LateMinutes"] = item.LateMinutes.ToString(),
                ["LateHours"] = Math.Round(item.LateMinutes / 60m, 2).ToString("0.##"),
                ["EarlyLeaveMinutes"] = item.EarlyLeaveMinutes?.ToString() ?? string.Empty,
                ["EarlyLeaveHours"] = item.EarlyLeaveMinutes.HasValue
                    ? Math.Round(item.EarlyLeaveMinutes.Value / 60m, 2).ToString("0.##")
                    : string.Empty,
                ["CalculatedStatus"] = item.CalculatedStatus,
                ["OriginalStatus"] = item.OriginalStatus,
                ["LeaveType"] = item.LeaveType ?? string.Empty,
                ["Holiday"] = item.HolidayName ?? string.Empty,
                ["Notes"] = item.Notes ?? string.Empty
            };

            rows.Add(FilterColumns(allValues, selectedKeys));
        }

        var summary = new Dictionary<string, string>
        {
            ["Total Rows"] = materialized.Count.ToString(),
            ["Present"] = materialized.Count(x => x.CalculatedStatus.Equals("Present", StringComparison.OrdinalIgnoreCase)).ToString(),
            ["Late"] = materialized.Count(x => x.CalculatedStatus.Equals("Late", StringComparison.OrdinalIgnoreCase)).ToString(),
            ["Absent"] = materialized.Count(x => x.CalculatedStatus.Equals("Absent", StringComparison.OrdinalIgnoreCase)).ToString(),
            ["Missing Out"] = materialized.Count(x => x.CalculatedStatus.Equals("Missing Check Out", StringComparison.OrdinalIgnoreCase)).ToString(),
            ["Weekly Off"] = materialized.Count(x => x.CalculatedStatus.Equals("Weekly Off", StringComparison.OrdinalIgnoreCase)).ToString()
        };

        return new ReportBuilderResultViewModel
        {
            ReportType = "Attendance",
            Columns = availableColumns.Where(x => selectedKeys.Contains(x.Key, StringComparer.OrdinalIgnoreCase)).ToList(),
            Rows = rows,
            Summary = summary
        };
    }

    private static Dictionary<string, string> FilterColumns(
        Dictionary<string, string> allValues,
        List<string> selectedKeys)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in selectedKeys)
        {
            row[key] = allValues.TryGetValue(key, out var value)
                ? value
                : string.Empty;
        }

        return row;
    }

    private static List<ReportBuilderColumnViewModel> GetEmployeeColumns()
    {
        return new()
        {
            Col("EmployeeNo", "Employee Code"),
            Col("EmployeeName", "Employee Name"),
            Col("Branch", "Branch"),
            Col("BranchCode", "Branch Code"),
            Col("Department", "Department"),
            Col("HireDate", "Hire Date"),
            Col("BirthDate", "Birth Date"),
            Col("NationalId", "National ID"),
            Col("Phone", "Phone"),
            Col("Email", "Email"),
            Col("IsActive", "Active")
        };
    }

    private static List<ReportBuilderColumnViewModel> GetAttendanceColumns()
    {
        return new()
        {
            Col("Date", "Date"),
            Col("EmployeeNo", "Employee Code"),
            Col("EmployeeName", "Employee Name"),
            Col("Branch", "Branch"),
            Col("BranchCode", "Branch Code"),
            Col("Department", "Department"),
            Col("Shift", "Shift"),
            Col("WeeklyOff", "Weekly Off"),
            Col("CheckIn", "Check In"),
            Col("CheckOut", "Check Out"),
            Col("WorkHours", "Work Hours"),
            Col("LateMinutes", "Late Minutes"),
            Col("LateHours", "Late Hours"),
            Col("EarlyLeaveMinutes", "Early Leave Minutes"),
            Col("EarlyLeaveHours", "Early Leave Hours"),
            Col("CalculatedStatus", "Calculated Status"),
            Col("OriginalStatus", "Original Status"),
            Col("LeaveType", "Leave Type"),
            Col("Holiday", "Holiday"),
            Col("Notes", "Notes")
        };
    }

    private static List<string> GetDefaultColumns(string reportType)
    {
        return NormalizeReportType(reportType) == "Attendance"
            ? new List<string>
            {
                "Date",
                "EmployeeNo",
                "EmployeeName",
                "Branch",
                "Department",
                "Shift",
                "CheckIn",
                "CheckOut",
                "WorkHours",
                "LateMinutes",
                "EarlyLeaveMinutes",
                "CalculatedStatus"
            }
            : new List<string>
            {
                "EmployeeNo",
                "EmployeeName",
                "Branch",
                "Department",
                "HireDate",
                "IsActive"
            };
    }

    private static ReportBuilderColumnViewModel Col(string key, string displayName)
    {
        return new ReportBuilderColumnViewModel
        {
            Key = key,
            DisplayName = displayName
        };
    }

    private static string NormalizeReportType(string? reportType)
    {
        if (string.Equals(reportType, "Attendance", StringComparison.OrdinalIgnoreCase))
            return "Attendance";

        return "Employees";
    }
}
