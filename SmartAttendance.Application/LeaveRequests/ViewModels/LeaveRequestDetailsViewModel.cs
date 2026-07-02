using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.LeaveRequests.ViewModels;

public class LeaveRequestDetailsViewModel
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }

    public string EmployeeNo { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public LeaveType LeaveType { get; set; }

    public LeaveStatus Status { get; set; }

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public int TotalDays { get; set; }

    public string? Reason { get; set; }
}
