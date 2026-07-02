using AutoMapper;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Application.LeaveRequests.Services;
using SmartAttendance.Application.LeaveRequests.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Infrastructure.Services;

public class LeaveRequestService : ILeaveRequestService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public LeaveRequestService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<LeaveRequestListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var leaveRequests = await _unitOfWork.LeaveRequests.GetAllAsync();
        var employees = await _unitOfWork.Employees.GetAllAsync();

        var employeeLookup = employees.ToDictionary(x => x.Id, x => new
        {
            x.EmployeeNo,
            x.FullName
        });

        var result = leaveRequests.Select(leaveRequest =>
        {
            var model = _mapper.Map<LeaveRequestListViewModel>(leaveRequest);

            if (employeeLookup.TryGetValue(leaveRequest.EmployeeId, out var employee))
            {
                model.EmployeeNo = employee.EmployeeNo;
                model.EmployeeName = employee.FullName;
            }

            model.TotalDays = leaveRequest.ToDate.DayNumber - leaveRequest.FromDate.DayNumber + 1;

            return model;
        });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.EmployeeNo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.EmployeeName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.LeaveType.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Status.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.Reason != null && x.Reason.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)));
        }

        return result
            .OrderByDescending(x => x.FromDate)
            .ThenBy(x => x.EmployeeName)
            .ToList();
    }

    public async Task<LeaveRequestDetailsViewModel?> GetByIdAsync(int id)
    {
        var leaveRequest = await _unitOfWork.LeaveRequests.GetByIdAsync(id);

        if (leaveRequest == null)
            return null;

        var employee = await _unitOfWork.Employees.GetByIdAsync(leaveRequest.EmployeeId);

        var model = _mapper.Map<LeaveRequestDetailsViewModel>(leaveRequest);

        model.EmployeeNo = employee?.EmployeeNo ?? string.Empty;
        model.EmployeeName = employee?.FullName ?? string.Empty;
        model.TotalDays = leaveRequest.ToDate.DayNumber - leaveRequest.FromDate.DayNumber + 1;

        return model;
    }

    public async Task<LeaveRequestEditViewModel?> GetEditByIdAsync(int id)
    {
        var leaveRequest = await _unitOfWork.LeaveRequests.GetByIdAsync(id);

        if (leaveRequest == null)
            return null;

        return _mapper.Map<LeaveRequestEditViewModel>(leaveRequest);
    }

    public async Task<bool> CreateAsync(LeaveRequestCreateViewModel model)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(model.EmployeeId);

        if (employee == null)
            return false;

        if (model.ToDate < model.FromDate)
            return false;

        var leaveRequest = _mapper.Map<LeaveRequest>(model);

        // HR enters approved leave directly. No Pending workflow on Create page.
        leaveRequest.Status = LeaveStatus.Approved;

        await _unitOfWork.LeaveRequests.AddAsync(leaveRequest);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(LeaveRequestEditViewModel model)
    {
        var leaveRequest = await _unitOfWork.LeaveRequests.GetByIdAsync(model.Id);

        if (leaveRequest == null)
            return false;

        var employee = await _unitOfWork.Employees.GetByIdAsync(model.EmployeeId);

        if (employee == null)
            return false;

        if (model.ToDate < model.FromDate)
            return false;

        leaveRequest.EmployeeId = model.EmployeeId;
        leaveRequest.LeaveType = model.LeaveType;
        leaveRequest.Status = model.Status;
        leaveRequest.FromDate = model.FromDate;
        leaveRequest.ToDate = model.ToDate;
        leaveRequest.Reason = model.Reason;

        _unitOfWork.LeaveRequests.Update(leaveRequest);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var leaveRequest = await _unitOfWork.LeaveRequests.GetByIdAsync(id);

        if (leaveRequest == null)
            return false;

        _unitOfWork.LeaveRequests.Delete(leaveRequest);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<IEnumerable<EmployeeListViewModel>> GetEmployeesForDropdownAsync()
    {
        var employees = await _unitOfWork.Employees.GetAllAsync();

        return employees
            .Where(x => x.IsActive)
            .OrderBy(x => x.FullName)
            .Select(x => new EmployeeListViewModel
            {
                Id = x.Id,
                EmployeeNo = x.EmployeeNo,
                FullName = x.FullName,
                NationalId = x.NationalId,
                Phone = x.Phone,
                Email = x.Email,
                HireDate = x.HireDate,
                BirthDate = x.BirthDate,
                IsActive = x.IsActive,
                DepartmentId = x.DepartmentId
            })
            .ToList();
    }
}
