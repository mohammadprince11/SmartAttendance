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
                (x.Address != null && x.Address.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                x.CompanyName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return result.OrderBy(x => x.Name).ToList();
    }

    public async Task<BranchDetailsViewModel?> GetByIdAsync(int id)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(id);

        if (branch == null)
            return null;

        var company = await _unitOfWork.Companies.GetByIdAsync(branch.CompanyId);

        var model = _mapper.Map<BranchDetailsViewModel>(branch);
        model.CompanyName = company?.Name ?? string.Empty;

        return model;
    }

    public async Task<BranchEditViewModel?> GetEditByIdAsync(int id)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(id);

        if (branch == null)
            return null;

        return _mapper.Map<BranchEditViewModel>(branch);
    }

    public async Task<bool> CreateAsync(BranchCreateViewModel model)
    {
        if (await CodeExistsAsync(model.Code))
            return false;

        var company = await _unitOfWork.Companies.GetByIdAsync(model.CompanyId);

        if (company == null)
            return false;

        var branch = _mapper.Map<Branch>(model);

        await _unitOfWork.Branches.AddAsync(branch);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(BranchEditViewModel model)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(model.Id);

        if (branch == null)
            return false;

        var company = await _unitOfWork.Companies.GetByIdAsync(model.CompanyId);

        if (company == null)
            return false;

        var branches = await _unitOfWork.Branches.GetAllAsync();

        var duplicateCode = branches.Any(x =>
            x.Id != model.Id &&
            x.Code.Equals(model.Code, StringComparison.OrdinalIgnoreCase));

        if (duplicateCode)
            return false;

        branch.Code = model.Code;
        branch.Name = model.Name;
        branch.Address = model.Address;
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
            return false;

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
}