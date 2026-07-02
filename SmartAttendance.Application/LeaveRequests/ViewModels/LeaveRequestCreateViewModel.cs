using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.LeaveRequests.ViewModels;

public class LeaveRequestCreateViewModel
{
    public int EmployeeId { get; set; }

    public LeaveType LeaveType { get; set; }

    public DateOnly FromDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public DateOnly ToDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public string? Reason { get; set; }
}
