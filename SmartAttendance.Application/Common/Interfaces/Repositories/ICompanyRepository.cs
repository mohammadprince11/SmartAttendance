using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.Common.Interfaces.Repositories;

public interface ICompanyRepository : IGenericRepository<Company>
{
    Task<Company?> GetByCodeAsync(string code);

    Task<bool> ExistsByCodeAsync(string code);
}