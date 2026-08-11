using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Application.LeaveRequests.ViewModels;

namespace SmartAttendance.Application.LeaveRequests.Services;

/// <summary>
/// كل عملية تأخذ <see cref="LeaveRequestAccessScope"/> <b>إلزاميّاً وبلا قيمة
/// افتراضية</b> — فلا يمكن كتابة استدعاء جديد ينسى نطاق الشركة والملكية دون أن
/// يرفضه المُصرِّف. راجع AUTHZ-003.
/// </summary>
public interface ILeaveRequestService
{
    Task<IEnumerable<LeaveRequestListViewModel>> GetAllAsync(
        LeaveRequestAccessScope scope, string? searchTerm = null);

    Task<LeaveRequestDetailsViewModel?> GetByIdAsync(int id, LeaveRequestAccessScope scope);

    Task<LeaveRequestEditViewModel?> GetEditByIdAsync(int id, LeaveRequestAccessScope scope);

    Task<bool> CreateAsync(LeaveRequestCreateViewModel model, LeaveRequestAccessScope scope);

    Task<bool> UpdateAsync(LeaveRequestEditViewModel model, LeaveRequestAccessScope scope);

    Task<bool> DeleteAsync(int id, LeaveRequestAccessScope scope);

    Task<IEnumerable<EmployeeListViewModel>> GetEmployeesForDropdownAsync(
        LeaveRequestAccessScope scope);
}
