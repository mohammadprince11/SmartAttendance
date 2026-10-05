using SmartAttendance.Application.Companies.ViewModels;

namespace SmartAttendance.Application.Companies.Services;

public interface ICompanyService
{
    Task<IEnumerable<CompanyListViewModel>> GetAllAsync(int tenantId, string? searchTerm = null);

    Task<CompanyDetailsViewModel?> GetByIdAsync(int id, int tenantId);

    Task<CompanyEditViewModel?> GetEditByIdAsync(int id, int tenantId);

    Task<bool> CreateAsync(CompanyCreateViewModel model, int tenantId);

    Task<bool> UpdateAsync(CompanyEditViewModel model, int tenantId);

    Task<bool> DeleteAsync(int id, int tenantId);

    Task<bool> CodeExistsAsync(string code, int tenantId);
}
