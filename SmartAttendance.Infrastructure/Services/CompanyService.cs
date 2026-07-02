using AutoMapper;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Services;

public class CompanyService : ICompanyService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public CompanyService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<CompanyListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var companies = await _unitOfWork.Companies.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            companies = companies.Where(x =>
                x.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Code.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return _mapper.Map<IEnumerable<CompanyListViewModel>>(companies);
    }

    public async Task<CompanyDetailsViewModel?> GetByIdAsync(int id)
    {
        var company = await _unitOfWork.Companies.GetByIdAsync(id);

        if (company == null)
            return null;

        return _mapper.Map<CompanyDetailsViewModel>(company);
    }

    public async Task<CompanyEditViewModel?> GetEditByIdAsync(int id)
    {
        var company = await _unitOfWork.Companies.GetByIdAsync(id);

        if (company == null)
            return null;

        return _mapper.Map<CompanyEditViewModel>(company);
    }

    public async Task<bool> CreateAsync(CompanyCreateViewModel model)
    {
        if (await _unitOfWork.Companies.ExistsByCodeAsync(model.Code))
            return false;

        var company = _mapper.Map<Company>(model);

        await _unitOfWork.Companies.AddAsync(company);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(CompanyEditViewModel model)
    {
        var company = await _unitOfWork.Companies.GetByIdAsync(model.Id);

        if (company == null)
            return false;

        company.Code = model.Code;
        company.Name = model.Name;
        company.Email = model.Email;
        company.Phone = model.Phone;
        company.IsActive = model.IsActive;

        _unitOfWork.Companies.Update(company);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var company = await _unitOfWork.Companies.GetByIdAsync(id);

        if (company == null)
            return false;

        _unitOfWork.Companies.Delete(company);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> CodeExistsAsync(string code)
    {
        return await _unitOfWork.Companies.ExistsByCodeAsync(code);
    }
}