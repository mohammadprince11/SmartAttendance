using SmartAttendance.Application.Companies.ViewModels;

namespace SmartAttendance.Application.Companies.Services;

public interface ICompanyService
{
    Task<IEnumerable<CompanyListViewModel>> GetAllAsync(string? searchTerm = null);

    Task<CompanyDetailsViewModel?> GetByIdAsync(int id);

    Task<CompanyEditViewModel?> GetEditByIdAsync(int id);

    Task<bool> CreateAsync(CompanyCreateViewModel model);

    Task<bool> UpdateAsync(CompanyEditViewModel model);

    Task<bool> DeleteAsync(int id);

    Task<bool> CodeExistsAsync(string code);
}