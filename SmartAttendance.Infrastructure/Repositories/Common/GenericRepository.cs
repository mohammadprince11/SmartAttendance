using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Domain.Common;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Repositories.Common;

public class GenericRepository<TEntity> : IGenericRepository<TEntity>
    where TEntity : BaseEntity
{
    protected readonly ApplicationDbContext _context;
    protected readonly DbSet<TEntity> _dbSet;

    public GenericRepository(ApplicationDbContext context)
    {
        _context = context;
        _dbSet = context.Set<TEntity>();
    }

    public virtual async Task<TEntity?> GetByIdAsync(int id)
    {
        return await _dbSet
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
    }

    public virtual async Task<IEnumerable<TEntity>> GetAllAsync()
    {
        return await _dbSet
            .Where(x => !x.IsDeleted)
            .ToListAsync();
    }

    public virtual async Task AddAsync(TEntity entity)
    {
        await _dbSet.AddAsync(entity);
    }

    public virtual void Update(TEntity entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        _dbSet.Update(entity);
    }

    public virtual void Delete(TEntity entity)
    {
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        _dbSet.Update(entity);
    }

    public virtual async Task<bool> ExistsAsync(int id)
    {
        return await _dbSet
            .AnyAsync(x => x.Id == id && !x.IsDeleted);
    }
}