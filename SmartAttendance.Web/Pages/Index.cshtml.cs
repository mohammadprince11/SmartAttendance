using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IUnitOfWork _unitOfWork;

    public IndexModel(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public DashboardCards Cards { get; set; } = new();

    public List<RecentAttendanceRow> RecentAttendance { get; set; } = new();

    public List<UpcomingHolidayRow> UpcomingHolidays { get; set; } = new();

    public List<CurrentLeaveRow> CurrentLeaves { get; set; } = new();

    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public async Task OnGetAsync()
    {
        var companies = await _unitOfWork.Companies.GetAllAsync();
        var branches = await _unitOfWork.Branches.GetAllAsync();
        var departments = await _unitOfWork.Departments.GetAllAsync();
        var employees = await _unitOfWork.Employees.GetAllAsync();
        var devices = await _unitOfWork.Devices.GetAllAsync();
        var shifts = await _unitOfWork.Shifts.GetAllAsync();
        var attendanceRecords = await _unitOfWork.AttendanceRecords.GetAllAsync();
        var holidays = await _unitOfWork.Holidays.GetAllAsync();
        var leaveRequests = await _unitOfWork.LeaveRequests.GetAllAsync();

        var employeeLookup = employees.ToDictionary(x => x.Id);

        var todayRecords = attendanceRecords
            .Where(x => x.AttendanceDate == Today)
            .ToList();

        var todayLeaves = leaveRequests
            .Where(x =>
                x.Status == LeaveStatus.Approved &&
                x.FromDate <= Today &&
                x.ToDate >= Today)
            .ToList();

        var todayHoliday = holidays.FirstOrDefault(x =>
            x.HolidayDate == Today ||
            (x.IsRecurring && x.HolidayDate.Month == Today.Month && x.HolidayDate.Day == Today.Day));

        Cards = new DashboardCards
        {
            CompaniesCount = companies.Count(),
            BranchesCount = branches.Count(),
            DepartmentsCount = departments.Count(),
            EmployeesCount = employees.Count(),
            ActiveEmployeesCount = employees.Count(x => x.IsActive),
            DevicesCount = devices.Count(),
            ActiveDevicesCount = devices.Count(x => x.IsActive),
            ShiftsCount = shifts.Count(),
            AttendanceRecordsCount = attendanceRecords.Count(),
            HolidaysCount = holidays.Count(),
            ApprovedLeavesCount = leaveRequests.Count(x => x.Status == LeaveStatus.Approved),

            TodayPresentCount = todayRecords.Count(x =>
                x.Status == AttendanceStatus.Present ||
                x.Status == AttendanceStatus.Late),

            TodayLeaveCount = todayLeaves.Count,
            TodayHolidayCount = todayHoliday == null ? 0 : 1,

            TodayMissingCheckOutCount = todayRecords.Count(x => !x.CheckOut.HasValue)
        };

        RecentAttendance = attendanceRecords
            .OrderByDescending(x => x.AttendanceDate)
            .ThenByDescending(x => x.CheckIn)
            .Take(10)
            .Select(x =>
            {
                employeeLookup.TryGetValue(x.EmployeeId, out var employee);

                return new RecentAttendanceRow
                {
                    AttendanceDate = x.AttendanceDate,
                    EmployeeNo = employee?.EmployeeNo ?? "-",
                    EmployeeName = employee?.FullName ?? "-",
                    CheckIn = x.CheckIn,
                    CheckOut = x.CheckOut,
                    Status = x.Status.ToString(),
                    Source = x.Source.ToString()
                };
            })
            .ToList();

        UpcomingHolidays = holidays
            .Select(x => new UpcomingHolidayRow
            {
                Name = x.Name,
                HolidayDate = ResolveUpcomingHolidayDate(x, Today),
                IsRecurring = x.IsRecurring
            })
            .Where(x => x.HolidayDate >= Today)
            .OrderBy(x => x.HolidayDate)
            .Take(5)
            .ToList();

        CurrentLeaves = todayLeaves
            .OrderBy(x => x.FromDate)
            .Take(10)
            .Select(x =>
            {
                employeeLookup.TryGetValue(x.EmployeeId, out var employee);

                return new CurrentLeaveRow
                {
                    EmployeeNo = employee?.EmployeeNo ?? "-",
                    EmployeeName = employee?.FullName ?? "-",
                    LeaveType = x.LeaveType.ToString(),
                    FromDate = x.FromDate,
                    ToDate = x.ToDate
                };
            })
            .ToList();
    }

    private static DateOnly ResolveUpcomingHolidayDate(Holiday holiday, DateOnly today)
    {
        if (!holiday.IsRecurring)
            return holiday.HolidayDate;

        var thisYearDate = new DateOnly(today.Year, holiday.HolidayDate.Month, holiday.HolidayDate.Day);

        if (thisYearDate >= today)
            return thisYearDate;

        return new DateOnly(today.Year + 1, holiday.HolidayDate.Month, holiday.HolidayDate.Day);
    }
}

public class DashboardCards
{
    public int CompaniesCount { get; set; }

    public int BranchesCount { get; set; }

    public int DepartmentsCount { get; set; }

    public int EmployeesCount { get; set; }

    public int ActiveEmployeesCount { get; set; }

    public int DevicesCount { get; set; }

    public int ActiveDevicesCount { get; set; }

    public int ShiftsCount { get; set; }

    public int AttendanceRecordsCount { get; set; }

    public int HolidaysCount { get; set; }

    public int ApprovedLeavesCount { get; set; }

    public int TodayPresentCount { get; set; }

    public int TodayLeaveCount { get; set; }

    public int TodayHolidayCount { get; set; }

    public int TodayMissingCheckOutCount { get; set; }
}

public class RecentAttendanceRow
{
    public DateOnly AttendanceDate { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public DateTime CheckIn { get; set; }

    public DateTime? CheckOut { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;
}

public class UpcomingHolidayRow
{
    public string Name { get; set; } = string.Empty;

    public DateOnly HolidayDate { get; set; }

    public bool IsRecurring { get; set; }
}

public class CurrentLeaveRow
{
    public string EmployeeNo { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public string LeaveType { get; set; } = string.Empty;

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }
}
