using AutoMapper;
using SmartAttendance.Application.Branches.Services;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Services;

public class BranchService : IBranchService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public BranchService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<BranchListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var branches = await _unitOfWork.Branches.GetAllAsync();
        var companies = await _unitOfWork.Companies.GetAllAsync();

        var companyLookup = companies.ToDictionary(x => x.Id, x => x.Name);

        var result = branches.Select(branch =>
        {
            var model = _mapper.Map<BranchListViewModel>(branch);

            model.CompanyName = companyLookup.TryGetValue(branch.CompanyId, out var companyName)
                ? companyName
                : string.Empty;

            return model;
        });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Code.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.Address != null &&
                 x.Address.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                x.CompanyName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return result.OrderBy(x => x.CompanyName).ThenBy(x => x.Name).ToList();
    }

    public async Task<BranchDetailsViewModel?> GetByIdAsync(int id)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(id);

        if (branch == null)
        {
            return null;
        }

        var company = await _unitOfWork.Companies.GetByIdAsync(branch.CompanyId);

        var model = _mapper.Map<BranchDetailsViewModel>(branch);
        model.CompanyName = company?.Name ?? string.Empty;

        return model;
    }

    public async Task<BranchEditViewModel?> GetEditByIdAsync(int id)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(id);

        return branch == null
            ? null
            : _mapper.Map<BranchEditViewModel>(branch);
    }

    public async Task<bool> CreateAsync(BranchCreateViewModel model)
    {
        var normalizedName = model.Name?.Trim() ?? string.Empty;

        if (model.CompanyId <= 0 || string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        var company = await _unitOfWork.Companies.GetByIdAsync(model.CompanyId);

        if (company == null || !company.IsActive)
        {
            return false;
        }

        var branches = await _unitOfWork.Branches.GetAllAsync();
        var code = string.IsNullOrWhiteSpace(model.Code)
            ? GenerateUniqueSetupCode("BR", branches.Select(x => x.Code))
            : model.Code.Trim();

        var duplicate = branches.Any(x =>
            SameSetupCode(x.Code, code) ||
            (x.CompanyId == model.CompanyId &&
             x.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)));

        if (duplicate)
        {
            return false;
        }

        var branch = _mapper.Map<Branch>(model);
        branch.Code = code;
        branch.Name = normalizedName;
        branch.Address = string.IsNullOrWhiteSpace(model.Address)
            ? null
            : model.Address.Trim();
        branch.CompanyId = model.CompanyId;
        branch.IsActive = true;

        await _unitOfWork.Branches.AddAsync(branch);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(BranchEditViewModel model)
    {
        var normalizedName = model.Name?.Trim() ?? string.Empty;

        if (model.CompanyId <= 0 || string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        var branch = await _unitOfWork.Branches.GetByIdAsync(model.Id);

        if (branch == null)
        {
            return false;
        }

        var company = await _unitOfWork.Companies.GetByIdAsync(model.CompanyId);

        if (company == null)
        {
            return false;
        }

        if (model.IsActive && !company.IsActive)
        {
            return false;
        }

        if (branch.CompanyId != model.CompanyId)
        {
            var employees = await _unitOfWork.Employees.GetAllAsync();
            var devices = await _unitOfWork.Devices.GetAllAsync();
            var departments = await _unitOfWork.Departments.GetAllAsync();

            var hasLinkedData =
                employees.Any(x => x.BranchId == branch.Id) ||
                devices.Any(x => x.BranchId == branch.Id) ||
                departments.Any(x => x.BranchId == branch.Id);

            if (hasLinkedData)
            {
                return false;
            }
        }

        if (branch.IsActive && !model.IsActive)
        {
            var employees = await _unitOfWork.Employees.GetAllAsync();
            var hasActiveEmployees = employees.Any(x =>
                x.IsActive &&
                x.BranchId == branch.Id);

            if (hasActiveEmployees)
            {
                return false;
            }
        }

        var branches = await _unitOfWork.Branches.GetAllAsync();
        var code = string.IsNullOrWhiteSpace(model.Code)
            ? branch.Code
            : model.Code.Trim();

        var duplicate = branches.Any(x =>
            x.Id != model.Id &&
            (SameSetupCode(x.Code, code) ||
             (x.CompanyId == model.CompanyId &&
              x.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase))));

        if (duplicate)
        {
            return false;
        }

        branch.Code = code;
        branch.Name = normalizedName;
        branch.Address = string.IsNullOrWhiteSpace(model.Address)
            ? null
            : model.Address.Trim();
        branch.IsActive = model.IsActive;
        branch.CompanyId = model.CompanyId;

        _unitOfWork.Branches.Update(branch);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(id);

        if (branch == null)
        {
            return false;
        }

        var departments = await _unitOfWork.Departments.GetAllAsync();
        var devices = await _unitOfWork.Devices.GetAllAsync();
        var employees = await _unitOfWork.Employees.GetAllAsync();

        if (departments.Any(x => x.BranchId == id) ||
            devices.Any(x => x.BranchId == id) ||
            employees.Any(x => x.BranchId == id))
        {
            return false;
        }

        _unitOfWork.Branches.Delete(branch);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> CodeExistsAsync(string code)
    {
        var branches = await _unitOfWork.Branches.GetAllAsync();

        return branches.Any(x =>
            x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IEnumerable<CompanyListViewModel>> GetCompaniesForDropdownAsync()
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

    private static string GenerateUniqueSetupCode(
        string prefix,
        IEnumerable<string> existingCodes)
    {
        var codePrefix = $"{prefix}-";
        var existing = existingCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var maxNumber = existing
            .Select(code =>
            {
                if (!code.StartsWith(
                        codePrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                return int.TryParse(
                    code[codePrefix.Length..],
                    out var number)
                    ? number
                    : 0;
            })
            .DefaultIfEmpty(0)
            .Max();

        var nextNumber = maxNumber + 1;
        var code = $"{codePrefix}{nextNumber:000}";

        while (existing.Contains(code))
        {
            nextNumber++;
            code = $"{codePrefix}{nextNumber:000}";
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
