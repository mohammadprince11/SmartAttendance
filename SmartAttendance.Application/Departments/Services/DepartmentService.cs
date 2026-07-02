using AutoMapper;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Services;

public class DepartmentService : IDepartmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public DepartmentService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<DepartmentListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var departments = await _unitOfWork.Departments.GetAllAsync();
        var branches = await _unitOfWork.Branches.GetAllAsync();

        var branchLookup = branches.ToDictionary(x => x.Id, x => x.Name);

        var result = departments.Select(department =>
        {
            var model = _mapper.Map<DepartmentListViewModel>(department);

            model.BranchName = branchLookup.TryGetValue(department.BranchId, out var branchName)
                ? branchName
                : string.Empty;

            return model;
        });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Code.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.BranchName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return result.OrderBy(x => x.Name).ToList();
    }

    public async Task<DepartmentDetailsViewModel?> GetByIdAsync(int id)
    {
        var department = await _unitOfWork.Departments.GetByIdAsync(id);

        if (department == null)
            return null;

        var branch = await _unitOfWork.Branches.GetByIdAsync(department.BranchId);

        var model = _mapper.Map<DepartmentDetailsViewModel>(department);
        model.BranchName = branch?.Name ?? string.Empty;

        return model;
    }

    public async Task<DepartmentEditViewModel?> GetEditByIdAsync(int id)
    {
        var department = await _unitOfWork.Departments.GetByIdAsync(id);

        if (department == null)
            return null;

        return _mapper.Map<DepartmentEditViewModel>(department);
    }

    public async Task<bool> CreateAsync(DepartmentCreateViewModel model)
    {
        if (await CodeExistsAsync(model.Code))
            return false;

        var branch = await _unitOfWork.Branches.GetByIdAsync(model.BranchId);

        if (branch == null)
            return false;

        var department = _mapper.Map<Department>(model);

        await _unitOfWork.Departments.AddAsync(department);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(DepartmentEditViewModel model)
    {
        var department = await _unitOfWork.Departments.GetByIdAsync(model.Id);

        if (department == null)
            return false;

        var branch = await _unitOfWork.Branches.GetByIdAsync(model.BranchId);

        if (branch == null)
            return false;

        var departments = await _unitOfWork.Departments.GetAllAsync();

        var duplicateCode = departments.Any(x =>
            x.Id != model.Id &&
            x.Code.Equals(model.Code, StringComparison.OrdinalIgnoreCase));

        if (duplicateCode)
            return false;

        department.Code = model.Code;
        department.Name = model.Name;
        department.IsActive = model.IsActive;
        department.BranchId = model.BranchId;

        _unitOfWork.Departments.Update(department);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var department = await _unitOfWork.Departments.GetByIdAsync(id);

        if (department == null)
            return false;

        _unitOfWork.Departments.Delete(department);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> CodeExistsAsync(string code)
    {
        var departments = await _unitOfWork.Departments.GetAllAsync();

        return departments.Any(x =>
            x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IEnumerable<BranchListViewModel>> GetBranchesForDropdownAsync()
    {
        var branches = await _unitOfWork.Branches.GetAllAsync();

        return branches
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new BranchListViewModel
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Address = x.Address,
                IsActive = x.IsActive,
                CompanyId = x.CompanyId
            })
            .ToList();
    }
}