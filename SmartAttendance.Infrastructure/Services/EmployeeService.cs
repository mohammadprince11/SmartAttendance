using AutoMapper;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Services;

public class EmployeeService : IEmployeeService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public EmployeeService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<EmployeeListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var employees = await _unitOfWork.Employees.GetAllAsync();
        var departments = await _unitOfWork.Departments.GetAllAsync();
        var branches = await _unitOfWork.Branches.GetAllAsync();

        var departmentLookup = departments.ToDictionary(x => x.Id, x => x);
        var branchLookup = branches.ToDictionary(x => x.Id, x => x.Name);

        var result = employees.Select(employee =>
        {
            var model = _mapper.Map<EmployeeListViewModel>(employee);

            if (departmentLookup.TryGetValue(employee.DepartmentId, out var department))
            {
                model.DepartmentCode = department.Code;
                model.DepartmentName = department.Name;
                model.BranchName = branchLookup.TryGetValue(department.BranchId, out var branchName)
                    ? branchName
                    : string.Empty;
            }

            return model;
        });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.EmployeeNo.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.FullName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.NationalId != null && x.NationalId.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                (x.Phone != null && x.Phone.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                (x.Email != null && x.Email.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                (x.Position != null && x.Position.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                x.DepartmentCode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.DepartmentName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.BranchName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return result
            .OrderBy(x => x.FullName)
            .ToList();
    }

    public async Task<EmployeeDetailsViewModel?> GetByIdAsync(int id)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(id);

        if (employee == null)
            return null;

        var department = await _unitOfWork.Departments.GetByIdAsync(employee.DepartmentId);
        var branch = department == null ? null : await _unitOfWork.Branches.GetByIdAsync(department.BranchId);

        var model = _mapper.Map<EmployeeDetailsViewModel>(employee);
        model.DepartmentName = department?.Name ?? string.Empty;
        model.BranchName = branch?.Name ?? string.Empty;

        return model;
    }

    public async Task<EmployeeEditViewModel?> GetEditByIdAsync(int id)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(id);

        if (employee == null)
            return null;

        return _mapper.Map<EmployeeEditViewModel>(employee);
    }

    public async Task<bool> CreateAsync(EmployeeCreateViewModel model)
    {
        if (await EmployeeNoExistsAsync(model.EmployeeNo))
            return false;

        var department = await _unitOfWork.Departments.GetByIdAsync(model.DepartmentId);

        if (department == null)
            return false;

        var employee = _mapper.Map<Employee>(model);

        await _unitOfWork.Employees.AddAsync(employee);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(EmployeeEditViewModel model)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(model.Id);

        if (employee == null)
            return false;

        var department = await _unitOfWork.Departments.GetByIdAsync(model.DepartmentId);

        if (department == null)
            return false;

        var employees = await _unitOfWork.Employees.GetAllAsync();

        var duplicateEmployeeNo = employees.Any(x =>
            x.Id != model.Id &&
            x.EmployeeNo.Equals(model.EmployeeNo, StringComparison.OrdinalIgnoreCase));

        if (duplicateEmployeeNo)
            return false;

        employee.EmployeeNo = model.EmployeeNo;
        employee.FullName = model.FullName;
        employee.NationalId = model.NationalId;
        employee.Phone = model.Phone;
        employee.Email = model.Email;
        employee.Position = model.Position;
        employee.HireDate = model.HireDate;
        employee.BirthDate = model.BirthDate;
        employee.IsActive = model.IsActive;
        employee.DepartmentId = model.DepartmentId;

        _unitOfWork.Employees.Update(employee);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(id);

        if (employee == null)
            return false;

        _unitOfWork.Employees.Delete(employee);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> EmployeeNoExistsAsync(string employeeNo)
    {
        var employees = await _unitOfWork.Employees.GetAllAsync();

        return employees.Any(x =>
            x.EmployeeNo.Equals(employeeNo, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IEnumerable<DepartmentListViewModel>> GetDepartmentsForDropdownAsync()
    {
        var departments = await _unitOfWork.Departments.GetAllAsync();
        var branches = await _unitOfWork.Branches.GetAllAsync();

        var branchLookup = branches.ToDictionary(x => x.Id, x => x.Name);

        return departments
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new DepartmentListViewModel
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                IsActive = x.IsActive,
                BranchId = x.BranchId,
                BranchName = branchLookup.TryGetValue(x.BranchId, out var branchName)
                    ? branchName
                    : string.Empty
            })
            .ToList();
    }
}
