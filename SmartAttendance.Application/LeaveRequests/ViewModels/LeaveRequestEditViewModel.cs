using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.LeaveRequests.ViewModels;

public class LeaveRequestEditViewModel
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public LeaveType LeaveType { get; set; }

    public LeaveStatus Status { get; set; }

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public string? Reason { get; set; }
}
