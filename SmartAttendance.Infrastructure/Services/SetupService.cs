using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Setup.Services;
using SmartAttendance.Application.Setup.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Infrastructure.Services;

public class SetupService : ISetupService
{
    private readonly IUnitOfWork _unitOfWork;

    public SetupService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<SystemSetupViewModel> GetSetupStatusAsync()
    {
        var companies = (await _unitOfWork.Companies.GetAllAsync()).ToList();
        var branches = (await _unitOfWork.Branches.GetAllAsync()).ToList();
        var departments = (await _unitOfWork.Departments.GetAllAsync()).ToList();
        var employees = (await _unitOfWork.Employees.GetAllAsync()).ToList();
        var devices = (await _unitOfWork.Devices.GetAllAsync()).ToList();
        var shifts = (await _unitOfWork.Shifts.GetAllAsync()).ToList();
        var employeeShifts = (await _unitOfWork.EmployeeShifts.GetAllAsync()).ToList();
        var attendanceRecords = (await _unitOfWork.AttendanceRecords.GetAllAsync()).ToList();
        var holidays = (await _unitOfWork.Holidays.GetAllAsync()).ToList();
        var leaveRequests = (await _unitOfWork.LeaveRequests.GetAllAsync()).ToList();

        var activeEmployees = employees.Where(x => x.IsActive).ToList();

        var employeesWithCurrentShift = employeeShifts
            .Where(x => x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= DateOnly.FromDateTime(DateTime.Today)))
            .Select(x => x.EmployeeId)
            .ToHashSet();

        var model = new SystemSetupViewModel
        {
            CompaniesCount = companies.Count,
            BranchesCount = branches.Count,
            DepartmentsCount = departments.Count,
            EmployeesCount = employees.Count,
            ActiveEmployeesCount = activeEmployees.Count,
            DevicesCount = devices.Count,
            ShiftsCount = shifts.Count,
            EmployeeShiftsCount = employeeShifts.Count,
            EmployeesWithoutCurrentShiftCount = activeEmployees.Count(x => !employeesWithCurrentShift.Contains(x.Id)),
            AttendanceRecordsCount = attendanceRecords.Count,
            HolidaysCount = holidays.Count,
            ApprovedLeavesCount = leaveRequests.Count(x => x.Status == LeaveStatus.Approved),
            Shifts = shifts
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new SetupDropdownItemViewModel
                {
                    Id = x.Id,
                    Text = $"{x.Code} - {x.Name} ({x.StartTime:HH:mm} - {x.EndTime:HH:mm})"
                })
                .ToList()
        };

        model.Steps = BuildSteps(model);

        return model;
    }

    public async Task<SetupActionResultViewModel> BulkAssignShiftAsync(BulkAssignShiftViewModel model)
    {
        var shift = await _unitOfWork.Shifts.GetByIdAsync(model.ShiftId);

        if (shift == null)
        {
            return new SetupActionResultViewModel
            {
                Success = false,
                Message = "Selected shift was not found."
            };
        }

        var employees = (await _unitOfWork.Employees.GetAllAsync())
            .Where(x => x.IsActive)
            .ToList();

        var employeeShifts = (await _unitOfWork.EmployeeShifts.GetAllAsync()).ToList();

        var currentShiftEmployeeIds = employeeShifts
            .Where(x => x.IsCurrent && (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= model.EffectiveFrom))
            .Select(x => x.EmployeeId)
            .ToHashSet();

        var targetEmployees = model.OnlyEmployeesWithoutCurrentShift
            ? employees.Where(x => !currentShiftEmployeeIds.Contains(x.Id)).ToList()
            : employees;

        var addedCount = 0;
        var weeklyOffDays = NormalizeWeeklyOffDays(model.WeeklyOffDays);

        foreach (var employee in targetEmployees)
        {
            if (!model.OnlyEmployeesWithoutCurrentShift)
            {
                var oldAssignments = employeeShifts
                    .Where(x => x.EmployeeId == employee.Id && x.IsCurrent)
                    .ToList();

                foreach (var assignment in oldAssignments)
                {
                    assignment.IsCurrent = false;
                    assignment.EffectiveTo = model.EffectiveFrom.AddDays(-1);
                    _unitOfWork.EmployeeShifts.Update(assignment);
                }
            }

            var newAssignment = new EmployeeShift
            {
                EmployeeId = employee.Id,
                ShiftId = shift.Id,
                EffectiveFrom = model.EffectiveFrom,
                EffectiveTo = null,
                IsCurrent = true,
                WeeklyOffDays = weeklyOffDays
            };

            await _unitOfWork.EmployeeShifts.AddAsync(newAssignment);
            addedCount++;
        }

        if (addedCount > 0)
            await _unitOfWork.SaveChangesAsync();

        return new SetupActionResultViewModel
        {
            Success = true,
            AffectedCount = addedCount,
            Message = addedCount == 0
                ? "No employees needed shift assignment."
                : $"Shift assigned successfully to {addedCount} employees."
        };
    }

    private static List<SetupStepViewModel> BuildSteps(SystemSetupViewModel model)
    {
        return new List<SetupStepViewModel>
        {
            new()
            {
                Order = 1,
                Title = "Companies",
                Status = model.IsCompaniesReady ? "Ready" : "Missing",
                Message = model.IsCompaniesReady ? $"{model.CompaniesCount} company records found." : "Import companies first.",
                LinkText = "Open Companies",
                LinkUrl = "/Companies"
            },
            new()
            {
                Order = 2,
                Title = "Branches",
                Status = model.IsBranchesReady ? "Ready" : "Missing",
                Message = model.IsBranchesReady ? $"{model.BranchesCount} branch records found." : "Import branches after companies.",
                LinkText = "Open Branches",
                LinkUrl = "/Branches"
            },
            new()
            {
                Order = 3,
                Title = "Departments",
                Status = model.IsDepartmentsReady ? "Ready" : "Missing",
                Message = model.IsDepartmentsReady ? $"{model.DepartmentsCount} department records found." : "Import departments after branches.",
                LinkText = "Open Departments",
                LinkUrl = "/Departments"
            },
            new()
            {
                Order = 4,
                Title = "Employees",
                Status = model.IsEmployeesReady ? "Ready" : "Missing",
                Message = model.IsEmployeesReady ? $"{model.EmployeesCount} employee records found." : "Import employees using unified EmployeeNo.",
                LinkText = "Open Employees",
                LinkUrl = "/Employees"
            },
            new()
            {
                Order = 5,
                Title = "Shifts",
                Status = model.IsShiftsReady ? "Ready" : "Missing",
                Message = model.IsShiftsReady ? $"{model.ShiftsCount} shift records found." : "Create or import at least one shift.",
                LinkText = "Open Shifts",
                LinkUrl = "/Shifts"
            },
            new()
            {
                Order = 6,
                Title = "Employee Shifts",
                Status = model.IsEmployeeShiftsReady ? "Ready" : "Need Action",
                Message = model.IsEmployeeShiftsReady
                    ? "All active employees have current shift assignment."
                    : $"{model.EmployeesWithoutCurrentShiftCount} active employees do not have a current shift.",
                LinkText = "Open Employee Shifts",
                LinkUrl = "/EmployeeShifts"
            },
            new()
            {
                Order = 7,
                Title = "Devices",
                Status = model.IsDevicesReady ? "Ready" : "Optional",
                Message = model.IsDevicesReady ? $"{model.DevicesCount} device records found." : "Import devices to track branch/device source.",
                LinkText = "Open Devices",
                LinkUrl = "/Devices"
            },
            new()
            {
                Order = 8,
                Title = "Attendance Import",
                Status = model.AttendanceRecordsCount > 0 ? "Ready" : "Waiting",
                Message = model.AttendanceRecordsCount > 0
                    ? $"{model.AttendanceRecordsCount} raw attendance records found."
                    : "Import attendance after employees and shifts are ready.",
                LinkText = "Open Attendance Import",
                LinkUrl = "/AttendanceImports"
            },
            new()
            {
                Order = 9,
                Title = "Processing & Reports",
                Status = model.AttendanceRecordsCount > 0 ? "Ready" : "Waiting",
                Message = model.AttendanceRecordsCount > 0
                    ? "You can process attendance and review reports."
                    : "Reports need attendance records first.",
                LinkText = "Open Reports",
                LinkUrl = "/AttendanceReports/Daily"
            }
        };
    }

    private static string? NormalizeWeeklyOffDays(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return string.Join(",",
            value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeDayName)
                .Distinct(StringComparer.OrdinalIgnoreCase));
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
}
