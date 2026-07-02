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
        var records = await _unitOfWork.AttendanceRecords.GetAllAsync();
        var employees = await _unitOfWork.Employees.GetAllAsync();
        var employeeShifts = await _unitOfWork.EmployeeShifts.GetAllAsync();
        var shifts = await _unitOfWork.Shifts.GetAllAsync();

        var employeeLookup = employees.ToDictionary(x => x.Id);
        var shiftLookup = shifts.ToDictionary(x => x.Id);

        if (fromDate.HasValue)
            records = records.Where(x => x.AttendanceDate >= fromDate.Value);

        if (toDate.HasValue)
            records = records.Where(x => x.AttendanceDate <= toDate.Value);

        var result = new List<AttendanceProcessingResultViewModel>();

        foreach (var record in records)
        {
            employeeLookup.TryGetValue(record.EmployeeId, out var employee);

            var employeeShift = FindApplicableEmployeeShift(
                employeeShifts,
                record.EmployeeId,
                record.AttendanceDate);

            Shift? shift = null;

            if (employeeShift != null)
                shiftLookup.TryGetValue(employeeShift.ShiftId, out shift);

            var processed = BuildProcessedRecord(record, employee, shift);

            result.Add(processed);
        }

        IEnumerable<AttendanceProcessingResultViewModel> filtered = result;

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            filtered = filtered.Where(x =>
                x.EmployeeNo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.EmployeeName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.ShiftCode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.ShiftName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.CalculatedStatus.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.OriginalStatus.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Source.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.Notes != null && x.Notes.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)));
        }

        return filtered
            .OrderByDescending(x => x.AttendanceDate)
            .ThenBy(x => x.EmployeeName)
            .ThenBy(x => x.CheckIn)
            .ToList();
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

    private static AttendanceProcessingResultViewModel BuildProcessedRecord(
        AttendanceRecord record,
        Employee? employee,
        Shift? shift)
    {
        var model = new AttendanceProcessingResultViewModel
        {
            AttendanceRecordId = record.Id,
            EmployeeId = record.EmployeeId,
            EmployeeNo = employee?.EmployeeNo ?? string.Empty,
            EmployeeName = employee?.FullName ?? string.Empty,
            AttendanceDate = record.AttendanceDate,
            CheckIn = record.CheckIn,
            CheckOut = record.CheckOut,
            Source = record.Source.ToString(),
            OriginalStatus = record.Status.ToString(),
            Notes = record.Notes,
            MissingCheckOut = !record.CheckOut.HasValue
        };

        if (shift == null)
        {
            model.CalculatedStatus = record.CheckOut.HasValue
                ? record.Status.ToString()
                : "Missing Check Out";

            if (record.CheckOut.HasValue)
                model.WorkingHours = CalculateWorkingHours(record.CheckIn, record.CheckOut.Value);

            return model;
        }

        model.ShiftCode = shift.Code;
        model.ShiftName = shift.Name;
        model.ShiftStartTime = shift.StartTime;
        model.ShiftEndTime = shift.EndTime;

        var scheduledStart = record.AttendanceDate.ToDateTime(shift.StartTime);
        var scheduledEnd = record.AttendanceDate.ToDateTime(shift.EndTime);

        if (shift.IsNightShift || shift.EndTime <= shift.StartTime)
            scheduledEnd = scheduledEnd.AddDays(1);

        model.LateMinutes = CalculateLateMinutes(record.CheckIn, scheduledStart, shift.GraceInMinutes);

        if (record.CheckOut.HasValue)
        {
            model.WorkingHours = CalculateWorkingHours(record.CheckIn, record.CheckOut.Value);
            model.EarlyLeaveMinutes = CalculateEarlyLeaveMinutes(record.CheckOut.Value, scheduledEnd, shift.GraceOutMinutes);
        }

        model.CalculatedStatus = CalculateStatus(record, model.LateMinutes, model.EarlyLeaveMinutes, model.MissingCheckOut);

        return model;
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
