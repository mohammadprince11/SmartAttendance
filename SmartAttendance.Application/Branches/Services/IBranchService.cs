using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Companies.ViewModels;

namespace SmartAttendance.Application.Branches.Services;

public interface IBranchService
{
    Task<IEnumerable<BranchListViewModel>> GetAllAsync(string? searchTerm = null);

    Task<BranchDetailsViewModel?> GetByIdAsync(int id);

    Task<BranchEditViewModel?> GetEditByIdAsync(int id);

    Task<bool> CreateAsync(BranchCreateViewModel model);

    Task<bool> UpdateAsync(BranchEditViewModel model);

    Task<bool> DeleteAsync(int id);

    Task<bool> CodeExistsAsync(string code);

    Task<IEnumerable<CompanyListViewModel>> GetCompaniesForDropdownAsync();
}