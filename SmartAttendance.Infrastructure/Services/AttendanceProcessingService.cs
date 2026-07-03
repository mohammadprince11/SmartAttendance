using SmartAttendance.Application.AttendanceProcessing.Services;
using SmartAttendance.Application.AttendanceProcessing.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Infrastructure.Services;

public class AttendanceProcessingService : IAttendanceProcessingService
{
    private readonly IUnitOfWork _unitOfWork;

    public AttendanceProcessingService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<AttendanceProcessingResultViewModel>> GetProcessedRecordsAsync(
        DateOnly? fromDate,
        DateOnly? toDate,
        string? searchTerm = null)
    {
        var startDate = fromDate ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var endDate = toDate ?? DateOnly.FromDateTime(DateTime.Today);

        if (endDate < startDate)
            return new List<AttendanceProcessingResultViewModel>();

        var records = await _unitOfWork.AttendanceRecords.GetAllAsync();
        var employees = await _unitOfWork.Employees.GetAllAsync();
        var employeeShifts = await _unitOfWork.EmployeeShifts.GetAllAsync();
        var shifts = await _unitOfWork.Shifts.GetAllAsync();
        var holidays = await _unitOfWork.Holidays.GetAllAsync();
        var leaveRequests = await _unitOfWork.LeaveRequests.GetAllAsync();

        var activeEmployees = employees
            .Where(x => x.IsActive)
            .OrderBy(x => x.FullName)
            .ToList();

        var recordsLookup = records
            .Where(x => x.AttendanceDate >= startDate && x.AttendanceDate <= endDate)
            .GroupBy(x => new { x.EmployeeId, x.AttendanceDate })
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(r => r.CheckIn).First());

        var shiftLookup = shifts.ToDictionary(x => x.Id);

        var result = new List<AttendanceProcessingResultViewModel>();

        foreach (var attendanceDate in GetDateRange(startDate, endDate))
        {
            var holiday = FindHoliday(holidays, attendanceDate);

            foreach (var employee in activeEmployees)
            {
                recordsLookup.TryGetValue(
                    new { EmployeeId = employee.Id, AttendanceDate = attendanceDate },
                    out var record);

                var employeeShift = FindApplicableEmployeeShift(
                    employeeShifts,
                    employee.Id,
                    attendanceDate);

                Shift? shift = null;

                if (employeeShift != null)
                    shiftLookup.TryGetValue(employeeShift.ShiftId, out shift);

                var isWeeklyOff = IsWeeklyOff(employeeShift, attendanceDate);

                var approvedLeave = FindApprovedLeave(
                    leaveRequests,
                    employee.Id,
                    attendanceDate);

                var processed = record != null
                    ? BuildProcessedRecord(record, employee, shift, holiday, approvedLeave, employeeShift?.WeeklyOffDays, isWeeklyOff)
                    : BuildGeneratedRecord(employee, attendanceDate, shift, holiday, approvedLeave, employeeShift?.WeeklyOffDays, isWeeklyOff);

                result.Add(processed);
            }
        }

        IEnumerable<AttendanceProcessingResultViewModel> filtered = result;

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            filtered = filtered.Where(x =>
                x.EmployeeNo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.EmployeeName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.ShiftCode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.ShiftName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.WeeklyOffDays != null && x.WeeklyOffDays.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                x.CalculatedStatus.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.OriginalStatus.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Source.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.LeaveType != null && x.LeaveType.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                (x.HolidayName != null && x.HolidayName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                (x.Notes != null && x.Notes.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)));
        }

        return filtered
            .OrderByDescending(x => x.AttendanceDate)
            .ThenBy(x => x.EmployeeName)
            .ToList();
    }

    private static IEnumerable<DateOnly> GetDateRange(DateOnly fromDate, DateOnly toDate)
    {
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
            yield return date;
    }

    private static EmployeeShift? FindApplicableEmployeeShift(
        IEnumerable<EmployeeShift> employeeShifts,
        int employeeId,
        DateOnly attendanceDate)
    {
        return employeeShifts
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.EffectiveFrom <= attendanceDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= attendanceDate))
            .OrderByDescending(x => x.IsCurrent)
            .ThenByDescending(x => x.EffectiveFrom)
            .FirstOrDefault();
    }

    private static Holiday? FindHoliday(IEnumerable<Holiday> holidays, DateOnly attendanceDate)
    {
        return holidays.FirstOrDefault(x =>
            x.HolidayDate == attendanceDate ||
            (x.IsRecurring &&
             x.HolidayDate.Month == attendanceDate.Month &&
             x.HolidayDate.Day == attendanceDate.Day));
    }

    private static LeaveRequest? FindApprovedLeave(
        IEnumerable<LeaveRequest> leaveRequests,
        int employeeId,
        DateOnly attendanceDate)
    {
        return leaveRequests
            .Where(x =>
                x.EmployeeId == employeeId &&
                x.Status == LeaveStatus.Approved &&
                x.FromDate <= attendanceDate &&
                x.ToDate >= attendanceDate)
            .OrderByDescending(x => x.FromDate)
            .FirstOrDefault();
    }

    private static AttendanceProcessingResultViewModel BuildGeneratedRecord(
        Employee employee,
        DateOnly attendanceDate,
        Shift? shift,
        Holiday? holiday,
        LeaveRequest? approvedLeave,
        string? weeklyOffDays,
        bool isWeeklyOff)
    {
        var model = new AttendanceProcessingResultViewModel
        {
            EmployeeId = employee.Id,
            EmployeeNo = employee.EmployeeNo,
            EmployeeName = employee.FullName,
            AttendanceDate = attendanceDate,
            Source = "System",
            OriginalStatus = "-",
            LateMinutes = 0,
            EarlyLeaveMinutes = null,
            MissingCheckOut = false,
            WeeklyOffDays = weeklyOffDays,
            IsWeeklyOff = isWeeklyOff
        };

        FillShiftData(model, shift);

        if (holiday != null)
        {
            model.CalculatedStatus = "Holiday";
            model.HolidayName = holiday.Name;
            model.Notes = holiday.Description;
            return model;
        }

        if (approvedLeave != null)
        {
            model.CalculatedStatus = "Leave";
            model.LeaveType = approvedLeave.LeaveType.ToString();
            model.Notes = approvedLeave.Reason;
            return model;
        }

        if (isWeeklyOff)
        {
            model.CalculatedStatus = "Weekly Off";
            model.Notes = $"Weekly off day: {attendanceDate.DayOfWeek}";
            return model;
        }

        model.CalculatedStatus = "Absent";

        return model;
    }

    private static AttendanceProcessingResultViewModel BuildProcessedRecord(
        AttendanceRecord record,
        Employee employee,
        Shift? shift,
        Holiday? holiday,
        LeaveRequest? approvedLeave,
        string? weeklyOffDays,
        bool isWeeklyOff)
    {
        var model = new AttendanceProcessingResultViewModel
        {
            AttendanceRecordId = record.Id,
            EmployeeId = record.EmployeeId,
            EmployeeNo = employee.EmployeeNo,
            EmployeeName = employee.FullName,
            AttendanceDate = record.AttendanceDate,
            CheckIn = record.CheckIn,
            CheckOut = record.CheckOut,
            Source = record.Source.ToString(),
            OriginalStatus = record.Status.ToString(),
            Notes = record.Notes,
            MissingCheckOut = !record.CheckOut.HasValue,
            HolidayName = holiday?.Name,
            LeaveType = approvedLeave?.LeaveType.ToString(),
            WeeklyOffDays = weeklyOffDays,
            IsWeeklyOff = isWeeklyOff
        };

        FillShiftData(model, shift);

        if (record.CheckOut.HasValue)
            model.WorkingHours = CalculateWorkingHours(record.CheckIn, record.CheckOut.Value);

        if (holiday != null)
        {
            model.CalculatedStatus = "Holiday Work";
            model.Notes = string.IsNullOrWhiteSpace(model.Notes)
                ? $"Worked on holiday: {holiday.Name}"
                : model.Notes;
            return model;
        }

        if (approvedLeave != null)
        {
            model.CalculatedStatus = "Leave Work";
            model.Notes = string.IsNullOrWhiteSpace(model.Notes)
                ? "Worked during approved leave."
                : model.Notes;
            return model;
        }

        if (isWeeklyOff)
        {
            model.LateMinutes = 0;
            model.EarlyLeaveMinutes = null;
            model.CalculatedStatus = record.CheckOut.HasValue
                ? "Work On Weekly Off"
                : "Weekly Off Missing Check Out";
            model.Notes = string.IsNullOrWhiteSpace(model.Notes)
                ? $"Worked on weekly off day: {record.AttendanceDate.DayOfWeek}"
                : model.Notes;
            return model;
        }

        if (shift == null)
        {
            model.CalculatedStatus = record.CheckOut.HasValue
                ? record.Status.ToString()
                : "Missing Check Out";

            return model;
        }

        var scheduledStart = record.AttendanceDate.ToDateTime(shift.StartTime);
        var scheduledEnd = record.AttendanceDate.ToDateTime(shift.EndTime);

        if (shift.IsNightShift || shift.EndTime <= shift.StartTime)
            scheduledEnd = scheduledEnd.AddDays(1);

        model.LateMinutes = CalculateLateMinutes(record.CheckIn, scheduledStart, shift.GraceInMinutes);

        if (record.CheckOut.HasValue)
        {
            model.EarlyLeaveMinutes = CalculateEarlyLeaveMinutes(record.CheckOut.Value, scheduledEnd, shift.GraceOutMinutes);
        }

        model.CalculatedStatus = CalculateStatus(record, model.LateMinutes, model.EarlyLeaveMinutes, model.MissingCheckOut);

        return model;
    }

    private static void FillShiftData(AttendanceProcessingResultViewModel model, Shift? shift)
    {
        if (shift == null)
            return;

        model.ShiftCode = shift.Code;
        model.ShiftName = shift.Name;
        model.ShiftStartTime = shift.StartTime;
        model.ShiftEndTime = shift.EndTime;
    }

    private static bool IsWeeklyOff(EmployeeShift? employeeShift, DateOnly attendanceDate)
    {
        if (employeeShift == null || string.IsNullOrWhiteSpace(employeeShift.WeeklyOffDays))
            return false;

        var currentDay = attendanceDate.DayOfWeek.ToString();

        return employeeShift.WeeklyOffDays
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(day => day.Equals(currentDay, StringComparison.OrdinalIgnoreCase));
    }

    private static int CalculateLateMinutes(DateTime checkIn, DateTime scheduledStart, int graceInMinutes)
    {
        var lateMinutes = (int)Math.Ceiling((checkIn - scheduledStart).TotalMinutes);

        if (lateMinutes <= graceInMinutes)
            return 0;

        return lateMinutes - graceInMinutes;
    }

    private static int CalculateEarlyLeaveMinutes(DateTime checkOut, DateTime scheduledEnd, int graceOutMinutes)
    {
        var earlyLeaveMinutes = (int)Math.Ceiling((scheduledEnd - checkOut).TotalMinutes);

        if (earlyLeaveMinutes <= graceOutMinutes)
            return 0;

        return earlyLeaveMinutes - graceOutMinutes;
    }

    private static decimal CalculateWorkingHours(DateTime checkIn, DateTime checkOut)
    {
        var totalHours = (decimal)(checkOut - checkIn).TotalHours;

        if (totalHours < 0)
            return 0;

        return Math.Round(totalHours, 2);
    }

    private static string CalculateStatus(
        AttendanceRecord record,
        int lateMinutes,
        int? earlyLeaveMinutes,
        bool missingCheckOut)
    {
        // Keep manual HR status untouched for these cases.
        if (record.Status is AttendanceStatus.Absent or AttendanceStatus.Leave or AttendanceStatus.Holiday)
            return record.Status.ToString();

        if (missingCheckOut)
            return "Missing Check Out";

        if (lateMinutes > 0)
            return "Late";

        if (earlyLeaveMinutes.HasValue && earlyLeaveMinutes.Value > 0)
            return "Early Leave";

        return "Present";
    }
}
