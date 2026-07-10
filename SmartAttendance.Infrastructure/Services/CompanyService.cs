using Microsoft.EntityFrameworkCore;
using AutoMapper;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Services;

public class CompanyService : ICompanyService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ApplicationDbContext _dbContext;

    public CompanyService(IUnitOfWork unitOfWork, IMapper mapper, ApplicationDbContext dbContext)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _dbContext = dbContext;
    }

        public async Task<IEnumerable<CompanyListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var companies = await _unitOfWork.Companies.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            companies = companies.Where(x =>
                x.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
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
        var seed = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var code = string.IsNullOrWhiteSpace(model.Code)
            ? $"COMP-{seed}"
            : model.Code.Trim();

        var baseCode = code;
        var counter = 1;

        while (await _unitOfWork.Companies.ExistsByCodeAsync(code))
        {
            code = $"{baseCode}-{counter}";
            counter++;
        }

        var company = _mapper.Map<Company>(model);
        company.Code = code;

        await _unitOfWork.Companies.AddAsync(company);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(CompanyEditViewModel model)
    {
        var company = await _unitOfWork.Companies.GetByIdAsync(model.Id);

        if (company == null)
            return false;

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
        var company = await _dbContext.Companies
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);

        if (company == null)
            return false;

        var hasLinkedBranches = await _dbContext.Branches
            .AnyAsync(x => x.CompanyId == id);

        if (!hasLinkedBranches)
        {
            _dbContext.Companies.Remove(company);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        company.IsDeleted = true;
        company.IsActive = false;
        company.UpdatedAt = DateTime.UtcNow;

        _dbContext.Companies.Update(company);
        await _dbContext.SaveChangesAsync();

        return true;
    }
    public async Task<bool> CodeExistsAsync(string code)
    {
        return await _unitOfWork.Companies.ExistsByCodeAsync(code);
    }
}