using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Departments.Services;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Infrastructure.Services;

public class DepartmentService : IDepartmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ApplicationDbContext _dbContext;

    public DepartmentService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ApplicationDbContext dbContext)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<DepartmentListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        await EnsureIndependentDepartmentSchemaAsync();

        var departments = await _unitOfWork.Departments.GetAllAsync();

        var result = departments.Select(department => new DepartmentListViewModel
        {
            Id = department.Id,
            Code = department.Code,
            Name = department.Name,
            IsActive = department.IsActive,
            BranchId = 0,
            BranchName = string.Empty
        });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Code.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
        }

        return result.OrderBy(x => x.Name).ToList();
    }

    public async Task<DepartmentDetailsViewModel?> GetByIdAsync(int id)
    {
        await EnsureIndependentDepartmentSchemaAsync();

        var department = await _unitOfWork.Departments.GetByIdAsync(id);

        if (department == null)
            return null;

        return new DepartmentDetailsViewModel
        {
            Id = department.Id,
            Code = department.Code,
            Name = department.Name,
            IsActive = department.IsActive,
            BranchId = 0,
            BranchName = string.Empty
        };
    }

    public async Task<DepartmentEditViewModel?> GetEditByIdAsync(int id)
    {
        await EnsureIndependentDepartmentSchemaAsync();

        var department = await _unitOfWork.Departments.GetByIdAsync(id);

        if (department == null)
            return null;

        return new DepartmentEditViewModel
        {
            Id = department.Id,
            Code = department.Code,
            Name = department.Name,
            IsActive = department.IsActive,
            BranchId = 0
        };
    }

    public async Task<bool> CreateAsync(DepartmentCreateViewModel model)
    {
        await EnsureIndependentDepartmentSchemaAsync();

        var departments = await _unitOfWork.Departments.GetAllAsync();
        var code = string.IsNullOrWhiteSpace(model.Code)
            ? GenerateUniqueSetupCode("DEP", departments.Select(x => x.Code))
            : model.Code.Trim();

        if (departments.Any(x => SameSetupCode(x.Code, code)))
            return false;

        var duplicateName = departments.Any(x =>
            x.Name.Equals(model.Name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (duplicateName)
            return false;

        var department = new Department
        {
            Code = code,
            Name = model.Name.Trim(),
            IsActive = true,
            BranchId = null
        };

        await _unitOfWork.Departments.AddAsync(department);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(DepartmentEditViewModel model)
    {
        await EnsureIndependentDepartmentSchemaAsync();

        var department = await _unitOfWork.Departments.GetByIdAsync(model.Id);

        if (department == null)
            return false;

        var departments = await _unitOfWork.Departments.GetAllAsync();
        var code = string.IsNullOrWhiteSpace(model.Code) ? department.Code : model.Code.Trim();

        var duplicateCode = departments.Any(x =>
            x.Id != model.Id &&
            SameSetupCode(x.Code, code));

        if (duplicateCode)
            return false;

        var duplicateName = departments.Any(x =>
            x.Id != model.Id &&
            x.Name.Equals(model.Name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (duplicateName)
            return false;

        department.Code = code;
        department.Name = model.Name.Trim();
        department.IsActive = model.IsActive;
        department.BranchId = null;

        _unitOfWork.Departments.Update(department);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        await EnsureIndependentDepartmentSchemaAsync();

        var department = await _unitOfWork.Departments.GetByIdAsync(id);

        if (department == null)
            return false;

        _unitOfWork.Departments.Delete(department);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> CodeExistsAsync(string code)
    {
        var departments = await _unitOfWork.Departments.GetAllAsync();

        return departments.Any(x =>
            x.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IEnumerable<BranchListViewModel>> GetBranchesForDropdownAsync()
    {
        var branches = await _unitOfWork.Branches.GetAllAsync();

        return branches
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new BranchListViewModel
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Address = x.Address,
                IsActive = x.IsActive,
                CompanyId = x.CompanyId
            })
            .ToList();
    }

    private async Task EnsureIndependentDepartmentSchemaAsync()
    {
        await _dbContext.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH('dbo.Departments', 'BranchId') IS NOT NULL
BEGIN
    DECLARE @dropFkSql NVARCHAR(MAX) = N'';

    SELECT @dropFkSql = @dropFkSql + N'ALTER TABLE dbo.Departments DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
    FROM sys.foreign_keys fk
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Departments')
      AND fk.referenced_object_id = OBJECT_ID(N'dbo.Branches');

    IF LEN(@dropFkSql) > 0
        EXEC sp_executesql @dropFkSql;

    DECLARE @dropIndexSql NVARCHAR(MAX) = N'';

    SELECT @dropIndexSql = @dropIndexSql + N'DROP INDEX ' + QUOTENAME(i.name) + N' ON dbo.Departments;'
    FROM sys.indexes i
    INNER JOIN sys.index_columns ic
        ON i.object_id = ic.object_id
       AND i.index_id = ic.index_id
    INNER JOIN sys.columns c
        ON ic.object_id = c.object_id
       AND ic.column_id = c.column_id
    WHERE i.object_id = OBJECT_ID(N'dbo.Departments')
      AND i.is_primary_key = 0
      AND i.name IS NOT NULL
      AND c.name = N'BranchId';

    IF LEN(@dropIndexSql) > 0
        EXEC sp_executesql @dropIndexSql;

    IF EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.Departments')
          AND name = N'BranchId'
          AND is_nullable = 0
    )
    BEGIN
        ALTER TABLE dbo.Departments ALTER COLUMN BranchId INT NULL;
    END
END;
");
    }

    private static string GenerateUniqueSetupCode(string prefix, IEnumerable<string> existingCodes)
    {
        var existing = existingCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seed = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var code = $"{prefix}-{seed}";
        var counter = 1;

        while (existing.Contains(code))
        {
            code = $"{prefix}-{seed}-{counter}";
            counter++;
        }

        return code;
    }

    private static bool SameSetupCode(string? left, string? right)
    {
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}