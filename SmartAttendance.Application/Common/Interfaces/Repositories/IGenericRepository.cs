using SmartAttendance.Domain.Common;

namespace SmartAttendance.Application.Common.Interfaces.Repositories;

public interface IGenericRepository<TEntity>
    where TEntity : BaseEntity
{
    /// <summary>مصدر قابل للتركيب لتطبيق حدود الوصول قبل تنفيذ الاستعلام.</summary>
    IQueryable<TEntity> Query();

    Task<TEntity?> GetByIdAsync(int id);

    Task<IEnumerable<TEntity>> GetAllAsync();

    Task AddAsync(TEntity entity);

    void Update(TEntity entity);

    void Delete(TEntity entity);

    Task<bool> ExistsAsync(int id);
}
