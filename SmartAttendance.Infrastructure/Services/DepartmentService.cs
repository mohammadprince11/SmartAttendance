using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Services;

public class DepartmentService : IDepartmentService
{
    private readonly IUnitOfWork _unitOfWork;

    public DepartmentService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<DepartmentListViewModel>> GetAllAsync(
        string? searchTerm = null)
    {
        var departments = await _unitOfWork.Departments.GetAllAsync();
        var companies = await _unitOfWork.Companies.GetAllAsync();

        var companyLookup = companies.ToDictionary(x => x.Id, x => x.Name);

        var result = departments.Select(department => new DepartmentListViewModel
        {
            Id = department.Id,
            Code = department.Code,
            Name = department.Name,
            IsActive = department.IsActive,
            CompanyId = department.CompanyId,
            CompanyName = companyLookup.TryGetValue(
                department.CompanyId,
                out var companyName)
                ? companyName
                : string.Empty,
            BranchId = 0,
            BranchName = string.Empty
        });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Code.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.CompanyName.Contains(
                    searchTerm,
                    StringComparison.OrdinalIgnoreCase));
        }

        return result
            .OrderBy(x => x.CompanyName)
            .ThenBy(x => x.Name)
            .ToList();
    }

    public async Task<DepartmentDetailsViewModel?> GetByIdAsync(int id)
    {
        var department = await _unitOfWork.Departments.GetByIdAsync(id);

        if (department == null)
        {
            return null;
        }

        var company = await _unitOfWork.Companies.GetByIdAsync(
            department.CompanyId);

        return new DepartmentDetailsViewModel
        {
            Id = department.Id,
            Code = department.Code,
            Name = department.Name,
            IsActive = department.IsActive,
            CompanyId = department.CompanyId,
            CompanyName = company?.Name ?? string.Empty,
            BranchId = 0,
            BranchName = string.Empty
        };
    }

    public async Task<DepartmentEditViewModel?> GetEditByIdAsync(int id)
    {
        var department = await _unitOfWork.Departments.GetByIdAsync(id);

        if (department == null)
        {
            return null;
        }

        return new DepartmentEditViewModel
        {
            Id = department.Id,
            Code = department.Code,
            Name = department.Name,
            IsActive = department.IsActive,
            CompanyId = department.CompanyId,
            BranchId = 0
        };
    }

    public async Task<bool> CreateAsync(DepartmentCreateViewModel model)
    {
        var normalizedName = model.Name?.Trim() ?? string.Empty;

        if (model.CompanyId <= 0 ||
            string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        var company = await _unitOfWork.Companies.GetByIdAsync(
            model.CompanyId);

        if (company == null || !company.IsActive)
        {
            return false;
        }

        var departments = await _unitOfWork.Departments.GetAllAsync();
        var code = string.IsNullOrWhiteSpace(model.Code)
            ? GenerateUniqueSetupCode("DEP", departments.Select(x => x.Code))
            : model.Code.Trim();

        var duplicate = departments.Any(x =>
            SameSetupCode(x.Code, code) ||
            (x.CompanyId == model.CompanyId &&
             x.Name.Equals(
                 normalizedName,
                 StringComparison.OrdinalIgnoreCase)));

        if (duplicate)
        {
            return false;
        }

        var department = new Department
        {
            Code = code,
            Name = normalizedName,
            IsActive = true,
            CompanyId = model.CompanyId,
            BranchId = null
        };

        await _unitOfWork.Departments.AddAsync(department);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(DepartmentEditViewModel model)
    {
        var normalizedName = model.Name?.Trim() ?? string.Empty;

        if (model.CompanyId <= 0 ||
            string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        var department = await _unitOfWork.Departments.GetByIdAsync(
            model.Id);

        if (department == null)
        {
            return false;
        }

        var company = await _unitOfWork.Companies.GetByIdAsync(
            model.CompanyId);

        if (company == null)
        {
            return false;
        }

        var departments = await _unitOfWork.Departments.GetAllAsync();
        var code = string.IsNullOrWhiteSpace(model.Code)
            ? department.Code
            : model.Code.Trim();

        var duplicate = departments.Any(x =>
            x.Id != model.Id &&
            (SameSetupCode(x.Code, code) ||
             (x.CompanyId == model.CompanyId &&
              x.Name.Equals(
                  normalizedName,
                  StringComparison.OrdinalIgnoreCase))));

        if (duplicate)
        {
            return false;
        }

        department.Code = code;
        department.Name = normalizedName;
        department.IsActive = model.IsActive;
        department.CompanyId = model.CompanyId;
        department.BranchId = null;

        _unitOfWork.Departments.Update(department);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var department = await _unitOfWork.Departments.GetByIdAsync(id);

        if (department == null)
        {
            return false;
        }

        var employees = await _unitOfWork.Employees.GetAllAsync();

        if (employees.Any(x => x.DepartmentId == id))
        {
            return false;
        }

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

    public async Task<IEnumerable<CompanyListViewModel>>
        GetCompaniesForDropdownAsync()
    {
        var companies = await _unitOfWork.Companies.GetAllAsync();

        return companies
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new CompanyListViewModel
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Email = x.Email,
                Phone = x.Phone,
                IsActive = x.IsActive
            })
            .ToList();
    }

    public async Task<IEnumerable<BranchListViewModel>>
        GetBranchesForDropdownAsync()
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

    private static string GenerateUniqueSetupCode(
        string prefix,
        IEnumerable<string> existingCodes)
    {
        var existing = existingCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seed = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var code = $"{prefix}-{seed}";
        var counter = 1;

        while (existing.Contains(code))
        {
            code = $"{prefix}-{seed}-{counter}";
            counter++;
        }

        return code;
    }

    private static bool SameSetupCode(string? left, string? right)
    {
        return string.Equals(
            left?.Trim(),
            right?.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}
