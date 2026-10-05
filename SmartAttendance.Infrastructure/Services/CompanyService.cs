using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Mapping;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Companies.Services;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Services;

public class CompanyService : ICompanyService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IModelMapper _mapper;
    private readonly ApplicationDbContext _dbContext;

    public CompanyService(IUnitOfWork unitOfWork, IModelMapper mapper, ApplicationDbContext dbContext)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<CompanyListViewModel>> GetAllAsync(int tenantId, string? searchTerm = null)
    {
        if (tenantId <= 0)
            return [];

        var query = _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            query = query.Where(x => x.Name.Contains(term) || x.Code.Contains(term));
        }

        var companies = await query.OrderBy(x => x.Name).ToListAsync();
        return _mapper.Map<IEnumerable<CompanyListViewModel>>(companies);
    }

    public async Task<CompanyDetailsViewModel?> GetByIdAsync(int id, int tenantId)
    {
        if (tenantId <= 0)
            return null;

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && !x.IsDeleted);

        if (company == null)
            return null;

        return _mapper.Map<CompanyDetailsViewModel>(company);
    }

    public async Task<CompanyEditViewModel?> GetEditByIdAsync(int id, int tenantId)
    {
        if (tenantId <= 0)
            return null;

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && !x.IsDeleted);

        if (company == null)
            return null;

        return _mapper.Map<CompanyEditViewModel>(company);
    }

    public async Task<bool> CreateAsync(CompanyCreateViewModel model, int tenantId)
    {
        if (tenantId <= 0)
            return false;

        var code = string.IsNullOrWhiteSpace(model.Code)
            ? await GenerateNextCompanyCodeAsync(tenantId)
            : model.Code.Trim();

        var baseCode = code;
        var counter = 1;

        while (await _dbContext.Companies
            .IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId && x.Code == code))
        {
            code = $"{baseCode}-{counter}";
            counter++;
        }

        var company = _mapper.Map<Company>(model);
        company.TenantId = tenantId;
        company.Code = code;
        model.Code = code;

        await _unitOfWork.Companies.AddAsync(company);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    private async Task<string> GenerateNextCompanyCodeAsync(int tenantId)
    {
        const string prefix = "COMP-";

        var existingCodes = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Code.StartsWith(prefix))
            .Select(x => x.Code)
            .ToListAsync();

        var maxNumber = existingCodes
            .Select(code => int.TryParse(code[prefix.Length..], out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        var nextNumber = maxNumber + 1;

        while (true)
        {
            var candidate = $"{prefix}{nextNumber:000}";
            var exists = await _dbContext.Companies
                .IgnoreQueryFilters()
                .AnyAsync(x => x.TenantId == tenantId && x.Code == candidate);

            if (!exists)
                return candidate;

            nextNumber++;
        }
    }

    public async Task<bool> UpdateAsync(CompanyEditViewModel model, int tenantId)
    {
        if (tenantId <= 0)
            return false;

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == model.Id && x.TenantId == tenantId && !x.IsDeleted);

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

    public async Task<bool> DeleteAsync(int id, int tenantId)
    {
        if (tenantId <= 0)
            return false;

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId && !x.IsDeleted);

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
    public async Task<bool> CodeExistsAsync(string code, int tenantId)
    {
        return tenantId > 0 && await _dbContext.Companies
            .IgnoreQueryFilters()
            .AnyAsync(x => x.TenantId == tenantId && x.Code == code && !x.IsDeleted);
    }
}
