using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Repositories.Common;

namespace SmartAttendance.Infrastructure.Repositories;

public class CompanyRepository : GenericRepository<Company>, ICompanyRepository
{
    public CompanyRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<Company?> GetByCodeAsync(string code)
    {
        return await _dbSet.FirstOrDefaultAsync(x => x.Code == code);
    }

    public async Task<bool> ExistsByCodeAsync(string code)
    {
        return await _dbSet.AnyAsync(x => x.Code == code);
    }
}