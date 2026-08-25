using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Application.LeaveRequests.Services;
using SmartAttendance.Application.LeaveRequests.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Infrastructure.Services;

/// <summary>
/// خدمة طلبات الإجازة — كل مسار قراءة وكتابة يفرض
/// <see cref="LeaveRequestAccessScope"/> قبل تسليم أي بيان أو تنفيذ أي تعديل
/// (AUTHZ-003). الفحص هنا لا بالصفحة: الصفحة تُنسى، والخدمة هي الطريق الوحيد
/// للبيانات.
/// </summary>
public class LeaveRequestService : ILeaveRequestService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public LeaveRequestService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<LeaveRequestListViewModel>> GetAllAsync(
        LeaveRequestAccessScope scope, string? searchTerm = null)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.IsDeniedAll)
        {
            return Array.Empty<LeaveRequestListViewModel>();
        }

        var query =
            from leaveRequest in _unitOfWork.LeaveRequests.Query().AsNoTracking()
            join employee in _unitOfWork.Employees.Query().AsNoTracking()
                on leaveRequest.EmployeeId equals employee.Id
            select new { LeaveRequest = leaveRequest, Employee = employee };

        if (scope.OnlyEmployeeId is { } employeeId)
            query = query.Where(row => row.Employee.Id == employeeId);
        if (!scope.IsUnrestricted)
        {
            var allowedCompanies = scope.AllowedCompanyIds.ToArray();
            query = query.Where(row => row.Employee.CompanyId.HasValue &&
                                       allowedCompanies.Contains(row.Employee.CompanyId.Value));
        }

        var scopedRows = await query.ToListAsync();
        var result = scopedRows.Select(row =>
            {
                var model = _mapper.Map<LeaveRequestListViewModel>(row.LeaveRequest);
                model.EmployeeNo = row.Employee.EmployeeNo;
                model.EmployeeName = row.Employee.FullName;
                model.TotalDays = row.LeaveRequest.ToDate.DayNumber - row.LeaveRequest.FromDate.DayNumber + 1;

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

    public async Task<LeaveRequestDetailsViewModel?> GetByIdAsync(
        int id, LeaveRequestAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var leaveRequest = await _unitOfWork.LeaveRequests.GetByIdAsync(id);

        if (leaveRequest == null)
            return null;

        var employee = await _unitOfWork.Employees.GetByIdAsync(leaveRequest.EmployeeId);

        // خارج النطاق ⟹ **null لا استثناء**: نفس ردّ «غير موجود» فلا يكشف الردُّ
        // وجودَ الطلب لمن لا يملكه (منع عدّ المعرّفات).
        if (!scope.Allows(employee?.CompanyId, leaveRequest.EmployeeId))
            return null;

        var model = _mapper.Map<LeaveRequestDetailsViewModel>(leaveRequest);

        model.EmployeeNo = employee?.EmployeeNo ?? string.Empty;
        model.EmployeeName = employee?.FullName ?? string.Empty;
        model.TotalDays = leaveRequest.ToDate.DayNumber - leaveRequest.FromDate.DayNumber + 1;

        return model;
    }

    public async Task<LeaveRequestEditViewModel?> GetEditByIdAsync(
        int id, LeaveRequestAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var leaveRequest = await _unitOfWork.LeaveRequests.GetByIdAsync(id);

        if (leaveRequest == null)
            return null;

        var employee = await _unitOfWork.Employees.GetByIdAsync(leaveRequest.EmployeeId);

        if (!scope.Allows(employee?.CompanyId, leaveRequest.EmployeeId))
            return null;

        return _mapper.Map<LeaveRequestEditViewModel>(leaveRequest);
    }

    public async Task<bool> CreateAsync(
        LeaveRequestCreateViewModel model, LeaveRequestAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var employee = await _unitOfWork.Employees.GetByIdAsync(model.EmployeeId);

        if (employee == null)
            return false;

        // الموظّف المستهدَف يأتي من النموذج: بلا هذا الفحص يُنشئ مستخدمُ شركةٍ
        // إجازةً لموظّف شركةٍ أخرى — كتابةٌ عابرة للشركات لا قراءة فقط.
        if (!scope.Allows(employee.CompanyId, employee.Id))
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

    public async Task<bool> UpdateAsync(
        LeaveRequestEditViewModel model, LeaveRequestAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var leaveRequest = await _unitOfWork.LeaveRequests.GetByIdAsync(model.Id);

        if (leaveRequest == null)
            return false;

        // فحصٌ مزدوج مقصود — الطرفان من مصدرين مختلفين:
        //   (١) المالك **الحالي** (من القاعدة): يمنع تعديل طلب شركةٍ أخرى.
        //   (٢) المالك **الجديد** (من النموذج): يمنع نقل الطلب إلى شركةٍ أخرى
        //       عبر تغيير EmployeeId — وهو overposting يهرّب صفّاً خارج النطاق.
        var currentOwner = await _unitOfWork.Employees.GetByIdAsync(leaveRequest.EmployeeId);

        if (!scope.Allows(currentOwner?.CompanyId, leaveRequest.EmployeeId))
            return false;

        var employee = await _unitOfWork.Employees.GetByIdAsync(model.EmployeeId);

        if (employee == null)
            return false;

        if (!scope.Allows(employee.CompanyId, employee.Id))
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

    public async Task<bool> DeleteAsync(int id, LeaveRequestAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var leaveRequest = await _unitOfWork.LeaveRequests.GetByIdAsync(id);

        if (leaveRequest == null)
            return false;

        var employee = await _unitOfWork.Employees.GetByIdAsync(leaveRequest.EmployeeId);

        // الحذف هو المسار الذي كان مكشوفاً بالكامل (AUTHZ-003): النموذج يرسل `id`
        // وحده، فلم يكن أي حارسٍ بالنظام يفحص مالك هذا المعرّف.
        if (!scope.Allows(employee?.CompanyId, leaveRequest.EmployeeId))
            return false;

        _unitOfWork.LeaveRequests.Delete(leaveRequest);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<IEnumerable<EmployeeListViewModel>> GetEmployeesForDropdownAsync(
        LeaveRequestAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.IsDeniedAll)
        {
            return Array.Empty<EmployeeListViewModel>();
        }

        var query = _unitOfWork.Employees.Query().AsNoTracking().Where(employee => employee.IsActive);
        if (scope.OnlyEmployeeId is { } employeeId)
            query = query.Where(employee => employee.Id == employeeId);
        if (!scope.IsUnrestricted)
        {
            var allowedCompanies = scope.AllowedCompanyIds.ToArray();
            query = query.Where(employee => employee.CompanyId.HasValue &&
                                            allowedCompanies.Contains(employee.CompanyId.Value));
        }

        return (await query
            .OrderBy(x => x.FullName)
            .ToListAsync())
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
