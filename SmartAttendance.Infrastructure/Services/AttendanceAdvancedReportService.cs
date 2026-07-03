using SmartAttendance.Application.AttendanceReports.Services;
using SmartAttendance.Application.AttendanceReports.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;

namespace SmartAttendance.Infrastructure.Services;

public class AttendanceAdvancedReportService : IAttendanceAdvancedReportService
{
    private readonly IUnitOfWork _unitOfWork;

    public AttendanceAdvancedReportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<DailyAttendanceReportViewModel> GetDailyReportAsync(DateOnly fromDate, DateOnly toDate, string? searchTerm)
    {
        var employees = (await _unitOfWork.Employees.GetAllAsync()).ToList();
        var departments = (await _unitOfWork.Departments.GetAllAsync()).ToList();
        var branches = (await _unitOfWork.Branches.GetAllAsync()).ToList();
        var records = (await _unitOfWork.AttendanceRecords.GetAllAsync())
            .Where(x => x.AttendanceDate >= fromDate && x.AttendanceDate <= toDate)
            .ToList();

        var departmentLookup = departments.ToDictionary(x => x.Id, x => x);
        var branchLookup = branches.ToDictionary(x => x.Id, x => x.Name);

        var rows = records.Select(record =>
        {
            var employee = employees.FirstOrDefault(x => x.Id == record.EmployeeId);

            string departmentName = string.Empty;
            string branchName = string.Empty;

            if (employee != null && departmentLookup.TryGetValue(employee.DepartmentId, out var department))
            {
                departmentName = department.Name;
                branchName = branchLookup.TryGetValue(department.BranchId, out var branch)
                    ? branch
                    : string.Empty;
            }

            return new DailyAttendanceReportRowViewModel
            {
                AttendanceDate = record.AttendanceDate,
                EmployeeNo = employee?.EmployeeNo ?? string.Empty,
                EmployeeName = employee?.FullName ?? string.Empty,
                DepartmentName = departmentName,
                BranchName = branchName,
                CheckIn = record.CheckIn,
                CheckOut = record.CheckOut,
                Status = record.Status.ToString(),
                Source = record.Source.ToString()
            };
        });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            rows = rows.Where(x =>
                x.EmployeeNo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.EmployeeName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.DepartmentName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.BranchName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Status.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return new DailyAttendanceReportViewModel
        {
            FromDate = fromDate,
            ToDate = toDate,
            SearchTerm = searchTerm,
            Rows = rows
                .OrderByDescending(x => x.AttendanceDate)
                .ThenBy(x => x.EmployeeName)
                .ToList()
        };
    }

    public async Task<MonthlyAttendanceReportViewModel> GetMonthlyReportAsync(DateOnly fromDate, DateOnly toDate, string? searchTerm)
    {
        var daily = await GetDailyReportAsync(fromDate, toDate, searchTerm);

        var rows = daily.Rows
            .GroupBy(x => new
            {
                x.EmployeeNo,
                x.EmployeeName,
                x.DepartmentName,
                x.BranchName
            })
            .Select(group => new MonthlyAttendanceReportRowViewModel
            {
                EmployeeNo = group.Key.EmployeeNo,
                EmployeeName = group.Key.EmployeeName,
                DepartmentName = group.Key.DepartmentName,
                BranchName = group.Key.BranchName,
                Present = group.Count(x => x.Status.Equals("Present", StringComparison.OrdinalIgnoreCase)),
                Late = group.Count(x => x.Status.Equals("Late", StringComparison.OrdinalIgnoreCase)),
                Absent = group.Count(x => x.Status.Equals("Absent", StringComparison.OrdinalIgnoreCase)),
                Leave = group.Count(x => x.Status.Equals("Leave", StringComparison.OrdinalIgnoreCase)),
                Holiday = group.Count(x => x.Status.Equals("Holiday", StringComparison.OrdinalIgnoreCase)),
                MissingCheckOut = group.Count(x => x.MissingCheckOut),
                TotalRecords = group.Count()
            })
            .OrderBy(x => x.EmployeeName)
            .ToList();

        return new MonthlyAttendanceReportViewModel
        {
            FromDate = fromDate,
            ToDate = toDate,
            SearchTerm = searchTerm,
            Rows = rows
        };
    }
}
