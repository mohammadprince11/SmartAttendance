using System.Data;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Infrastructure.Security;

namespace SmartAttendance.Infrastructure.Services;

public class EmployeeService : IEmployeeService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly ApplicationDbContext _dbContext;

    public EmployeeService(
        IUnitOfWork unitOfWork,
        IMapper mapper,
        ApplicationDbContext dbContext)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _dbContext = dbContext;
    }

    public async Task<EmployeePagedResultViewModel> GetPagedAsync(
        EmployeeListQueryViewModel query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageSize = NormalizePageSize(query.PageSize);
        var requestedPage = Math.Max(query.PageNumber, 1);
        var statusFilter = NormalizeStatusFilter(query.StatusFilter);
        var sortBy = NormalizeSortBy(query.SortBy);
        var searchTerm = string.IsNullOrWhiteSpace(query.SearchTerm)
            ? null
            : query.SearchTerm.Trim();

        var employeesQuery = _dbContext.Employees
            .AsNoTracking()
            .Where(x => !x.IsDeleted);

        employeesQuery = employeesQuery.ApplyPeopleDataScope(
            query.DataScope ?? PeopleDataScope.Unrestricted());

        if (query.CompanyId.HasValue &&
            query.CompanyId.Value > 0)
        {
            employeesQuery = employeesQuery.Where(x =>
                !x.Branch.IsDeleted &&
                x.Branch.CompanyId == query.CompanyId.Value);
        }
        else
        {
            employeesQuery = employeesQuery.Where(x => false);
        }

        var startOfYear = new DateOnly(DateTime.Today.Year, 1, 1);
        var startOfNextYear = startOfYear.AddYears(1);

        var employeeSummary = await employeesQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                TotalEmployees = group.Count(),
                ActiveEmployees = group.Count(x => x.IsActive),
                NewThisYear = group.Count(x =>
                    x.HireDate >= startOfYear &&
                    x.HireDate < startOfNextYear)
            })
            .FirstOrDefaultAsync();

        var totalEmployees = employeeSummary?.TotalEmployees ?? 0;
        var activeEmployees = employeeSummary?.ActiveEmployees ?? 0;
        var inactiveEmployees = totalEmployees - activeEmployees;
        var newThisYear = employeeSummary?.NewThisYear ?? 0;

        var filteredQuery = employeesQuery;

        // 🚀 هذا هو التعديل السحري الذي سيجعل البحث طلقة!
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            // نستخدم StartsWith للكود والهوية لتمكين الـ Index في قاعدة البيانات
            // ركزنا البحث على أهم الحقول لتقليل الضغط على الـ SQL Server
            filteredQuery = filteredQuery.Where(x =>
                x.EmployeeNo.StartsWith(searchTerm) ||
                x.FullName.Contains(searchTerm) ||
                (x.Phone != null && x.Phone.Contains(searchTerm)) ||
                (x.NationalId != null && x.NationalId.StartsWith(searchTerm))
            );
        }

        if (query.BranchId.HasValue && query.BranchId.Value > 0)
        {
            filteredQuery = filteredQuery.Where(x =>
                x.BranchId == query.BranchId.Value);
        }

        if (query.DepartmentId.HasValue && query.DepartmentId.Value > 0)
        {
            filteredQuery = filteredQuery.Where(x =>
                x.DepartmentId == query.DepartmentId.Value);
        }

        filteredQuery = statusFilter switch
        {
            "all" => filteredQuery,
            "inactive" => filteredQuery.Where(x => !x.IsActive),
            _ => filteredQuery.Where(x => x.IsActive)
        };

        filteredQuery = sortBy switch
        {
            "code" => filteredQuery
                .OrderBy(x => x.EmployeeNo)
                .ThenBy(x => x.FullName),
            "branch" => filteredQuery
                .OrderBy(x => x.Branch.Name)
                .ThenBy(x => x.FullName),
            "department" => filteredQuery
                .OrderBy(x => x.Department.Name)
                .ThenBy(x => x.FullName),
            "hiredate" => filteredQuery
                .OrderByDescending(x => x.HireDate)
                .ThenBy(x => x.FullName),
            "status" => filteredQuery
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.FullName),
            _ => filteredQuery
                .OrderBy(x => x.FullName)
                .ThenBy(x => x.EmployeeNo)
        };

        var hasResultFilters = !string.IsNullOrWhiteSpace(searchTerm) ||
                               (query.BranchId.HasValue &&
                                query.BranchId.Value > 0) ||
                               (query.DepartmentId.HasValue &&
                                query.DepartmentId.Value > 0);

        var filteredEmployees = hasResultFilters
            ? await filteredQuery.CountAsync()
            : statusFilter switch
            {
                "all" => totalEmployees,
                "inactive" => inactiveEmployees,
                _ => activeEmployees
            };

        var totalPages = filteredEmployees == 0
            ? 0
            : (int)Math.Ceiling(filteredEmployees / (double)pageSize);
        var pageNumber = totalPages == 0
            ? 1
            : Math.Min(requestedPage, totalPages);

        var pageEmployeeIds = await filteredQuery
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.Id)
            .ToListAsync();

        var pageEmployees = pageEmployeeIds.Count == 0
            ? new List<EmployeeListViewModel>()
            : await employeesQuery
                .Where(x => pageEmployeeIds.Contains(x.Id))
                .Select(x => new EmployeeListViewModel
                {
                    Id = x.Id,
                    EmployeeNo = x.EmployeeNo,
                    FullName = x.FullName,
                    Phone = x.Phone,
                    Email = x.Email,
                    Position = x.Position,
                    HireDate = x.HireDate,
                    IsActive = x.IsActive,
                    CompanyId = x.Branch.CompanyId,
                    BranchId = x.BranchId,
                    DepartmentId = x.DepartmentId,
                    DepartmentName = x.Department.IsDeleted
                        ? string.Empty
                        : x.Department.Name,
                    BranchName = x.Branch.IsDeleted
                        ? string.Empty
                        : x.Branch.Name
                })
                .ToListAsync();

        var pageEmployeesById = pageEmployees.ToDictionary(x => x.Id);
        var items = pageEmployeeIds
            .Where(pageEmployeesById.ContainsKey)
            .Select(id => pageEmployeesById[id])
            .ToList();

        return new EmployeePagedResultViewModel
        {
            Items = items,
            TotalEmployees = totalEmployees,
            FilteredEmployees = filteredEmployees,
            ActiveEmployees = activeEmployees,
            InactiveEmployees = inactiveEmployees,
            NewThisYear = newThisYear,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = totalPages
        };
    }

    public async Task<IEnumerable<EmployeeListViewModel>> GetAllAsync(
        string? searchTerm = null)
    {
        var employees = await _unitOfWork.Employees.GetAllAsync();
        var departments = await _unitOfWork.Departments.GetAllAsync();
        var branches = await _unitOfWork.Branches.GetAllAsync();
        var positions = await LoadPositionsAsync(includeInactive: true);

        var departmentLookup = departments.ToDictionary(x => x.Id, x => x);
        var branchLookup = branches.ToDictionary(x => x.Id, x => x.Name);
        var positionLookup = positions.ToDictionary(x => x.Id, x => x);

        var result = employees.Select(employee =>
        {
            var model = _mapper.Map<EmployeeListViewModel>(employee);
            model.BranchId = employee.BranchId;
            model.PositionId = employee.PositionId;

            if (departmentLookup.TryGetValue(
                employee.DepartmentId,
                out var department))
            {
                model.DepartmentCode = department.Code;
                model.DepartmentName = department.Name;
            }

            model.BranchName = branchLookup.TryGetValue(
                employee.BranchId,
                out var branchName)
                ? branchName
                : string.Empty;

            if (employee.PositionId.HasValue &&
                positionLookup.TryGetValue(
                    employee.PositionId.Value,
                    out var position))
            {
                model.Position = position.Name;
            }

            return model;
        });

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.EmployeeNo.Contains(
                    searchTerm,
                    StringComparison.OrdinalIgnoreCase) ||
                x.FullName.Contains(
                    searchTerm,
                    StringComparison.OrdinalIgnoreCase) ||
                (x.NationalId != null &&
                 x.NationalId.Contains(
                     searchTerm,
                     StringComparison.OrdinalIgnoreCase)) ||
                (x.Phone != null &&
                 x.Phone.Contains(
                     searchTerm,
                     StringComparison.OrdinalIgnoreCase)));
        }

        return result
            .OrderBy(x => x.FullName)
            .ToList();
    }

    public async Task<EmployeeDetailsViewModel?> GetByIdAsync(int id)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(id);

        if (employee == null)
        {
            return null;
        }

        var department = await _unitOfWork.Departments.GetByIdAsync(
            employee.DepartmentId);
        var branch = await _unitOfWork.Branches.GetByIdAsync(
            employee.BranchId);
        var position = employee.PositionId.HasValue
            ? await GetPositionAsync(employee.PositionId.Value)
            : null;

        var model = _mapper.Map<EmployeeDetailsViewModel>(employee);
        model.BranchId = employee.BranchId;
        model.PositionId = employee.PositionId;
        model.DepartmentName = department?.Name ?? string.Empty;
        model.BranchName = branch?.Name ?? string.Empty;

        if (position != null)
        {
            model.Position = position.Name;
        }

        return model;
    }

    public async Task<EmployeeEditViewModel?> GetEditByIdAsync(int id)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(id);

        if (employee == null)
        {
            return null;
        }

        var model = _mapper.Map<EmployeeEditViewModel>(employee);
        model.BranchId = employee.BranchId;
        model.PositionId = employee.PositionId;

        return model;
    }

    public async Task<bool> CreateAsync(EmployeeCreateViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.EmployeeNo) ||
            await EmployeeNoExistsAsync(model.EmployeeNo))
        {
            return false;
        }

        var department = await _unitOfWork.Departments.GetByIdAsync(
            model.DepartmentId);
        var branch = await _unitOfWork.Branches.GetByIdAsync(
            model.BranchId);
        var position = model.PositionId.HasValue
            ? await GetPositionAsync(model.PositionId.Value)
            : null;

        if (department == null ||
            branch == null ||
            !department.IsActive ||
            !branch.IsActive ||
            department.CompanyId != branch.CompanyId ||
            (model.PositionId.HasValue &&
             (position == null ||
              !position.IsActive ||
              position.CompanyId != branch.CompanyId)))
        {
            return false;
        }

        var employee = _mapper.Map<Employee>(model);
        employee.BranchId = model.BranchId;
        employee.DepartmentId = model.DepartmentId;
        employee.PositionId = model.PositionId;
        employee.Position = position?.Name;

        await _unitOfWork.Employees.AddAsync(employee);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(EmployeeEditViewModel model)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(model.Id);

        if (employee == null)
        {
            return false;
        }

        var department = await _unitOfWork.Departments.GetByIdAsync(
            model.DepartmentId);
        var branch = await _unitOfWork.Branches.GetByIdAsync(
            model.BranchId);
        var position = model.PositionId.HasValue
            ? await GetPositionAsync(model.PositionId.Value)
            : null;

        if (department == null ||
            branch == null ||
            department.CompanyId != branch.CompanyId ||
            (model.PositionId.HasValue &&
             (position == null ||
              !position.IsActive ||
              position.CompanyId != branch.CompanyId)))
        {
            return false;
        }

        // النقل إلى قسم أو موقع عمل جديد يتطلب أن يكون الهدف فعالاً؛
        // البقاء في نفس الجهة مسموح حتى لو تم إيقافها لاحقاً.
        if (model.BranchId != employee.BranchId && !branch.IsActive)
        {
            return false;
        }

        if (model.DepartmentId != employee.DepartmentId && !department.IsActive)
        {
            return false;
        }

        var duplicateEmployeeNo = await _dbContext.Employees
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id != model.Id &&
                !x.IsDeleted &&
                x.EmployeeNo == model.EmployeeNo);

        if (duplicateEmployeeNo)
        {
            return false;
        }

        // المدير المباشر: لا يكون الموظف نفسه، ويجب أن يكون فعالاً ومن نفس شركة موقع العمل الهدف.
        if (model.DirectManagerId.HasValue)
        {
            if (model.DirectManagerId.Value == model.Id)
            {
                return false;
            }

            var managerValid = await _dbContext.Employees
                .AsNoTracking()
                .AnyAsync(x =>
                    x.Id == model.DirectManagerId.Value &&
                    !x.IsDeleted &&
                    x.IsActive &&
                    x.Branch.CompanyId == branch.CompanyId);

            if (!managerValid)
            {
                return false;
            }
        }

        employee.EmployeeNo = model.EmployeeNo;
        employee.FullName = model.FullName;
        employee.NationalId = model.NationalId;
        employee.Phone = model.Phone;
        employee.Email = model.Email;
        employee.PositionId = model.PositionId;
        employee.Position = position?.Name;
        employee.HireDate = model.HireDate;
        employee.BirthDate = model.BirthDate;
        employee.MaritalStatus = model.MaritalStatus;
        employee.Gender = model.Gender;
        employee.Nationality = model.Nationality;
        employee.Country = model.Country;
        employee.IsActive = model.IsActive;
        employee.DirectManagerId = model.DirectManagerId;
        employee.BranchId = model.BranchId;
        employee.DepartmentId = model.DepartmentId;

        _unitOfWork.Employees.Update(employee);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var employee = await _unitOfWork.Employees.GetByIdAsync(id);

        if (employee == null)
        {
            return false;
        }

        _unitOfWork.Employees.Delete(employee);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> EmployeeNoExistsAsync(string employeeNo)
    {
        return await _dbContext.Employees
            .AsNoTracking()
            .AnyAsync(x =>
                !x.IsDeleted &&
                x.EmployeeNo == employeeNo);
    }

    public async Task<IEnumerable<DepartmentListViewModel>>
        GetDepartmentsForDropdownAsync(
            int? companyId = null,
            PeopleDataScope? dataScope = null)
    {
        var departmentsQuery = _dbContext.Departments
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.IsActive &&
                !x.Company.IsDeleted &&
                x.Company.IsActive);

        if (companyId.HasValue && companyId.Value > 0)
        {
            departmentsQuery = departmentsQuery.Where(x =>
                x.CompanyId == companyId.Value);
        }

        if (dataScope != null &&
            (!dataScope.IsUnrestricted || dataScope.HasAnyDenial))
        {
            var accessibleDepartmentIds = await _dbContext.Employees
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .ApplyPeopleDataScope(dataScope)
                .Select(x => x.DepartmentId)
                .Distinct()
                .ToListAsync();

            departmentsQuery = departmentsQuery.Where(x =>
                accessibleDepartmentIds.Contains(x.Id));
        }

        return await departmentsQuery
            .OrderBy(x => x.Company.Name)
            .ThenBy(x => x.Name)
            .Select(x => new DepartmentListViewModel
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                IsActive = x.IsActive,
                CompanyId = x.CompanyId,
                CompanyName = x.Company.Name,
                BranchId = x.BranchId ?? 0,
                BranchName = x.Branch == null || x.Branch.IsDeleted
                    ? string.Empty
                    : x.Branch.Name
            })
            .ToListAsync();
    }

    public async Task<IEnumerable<BranchListViewModel>>
        GetBranchesForDropdownAsync(
            int? companyId = null,
            PeopleDataScope? dataScope = null)
    {
        var branchesQuery = _dbContext.Branches
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted &&
                x.IsActive &&
                !x.Company.IsDeleted &&
                x.Company.IsActive);

        if (companyId.HasValue && companyId.Value > 0)
        {
            branchesQuery = branchesQuery.Where(x =>
                x.CompanyId == companyId.Value);
        }

        if (dataScope != null &&
            (!dataScope.IsUnrestricted || dataScope.HasAnyDenial))
        {
            var accessibleBranchIds = await _dbContext.Employees
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .ApplyPeopleDataScope(dataScope)
                .Select(x => x.BranchId)
                .Distinct()
                .ToListAsync();

            branchesQuery = branchesQuery.Where(x =>
                accessibleBranchIds.Contains(x.Id));
        }

        return await branchesQuery
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
            .ToListAsync();
    }

    public async Task<IReadOnlyList<int>> GetAccessibleCompanyIdsAsync(
        PeopleDataScope dataScope)
    {
        ArgumentNullException.ThrowIfNull(dataScope);

        return await _dbContext.Employees
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .ApplyPeopleDataScope(dataScope)
            .Select(x => x.Branch.CompanyId)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();
    }

    public async Task<IEnumerable<PositionOptionViewModel>>
        GetPositionsForDropdownAsync()
    {
        var positions = await LoadPositionsAsync(includeInactive: false);

        return positions
            .OrderBy(x => x.Name)
            .Select(x => new PositionOptionViewModel
            {
                Id = x.Id,
                CompanyId = x.CompanyId,
                Name = x.Name,
                IsActive = x.IsActive
            })
            .ToList();
    }

    private async Task<PositionLookupRow?> GetPositionAsync(int id)
    {
        var positions = await LoadPositionsAsync(includeInactive: true);
        return positions.FirstOrDefault(x => x.Id == id);
    }

    private async Task<List<PositionLookupRow>> LoadPositionsAsync(
        bool includeInactive)
    {
        var result = new List<PositionLookupRow>();
        var connection = _dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;

        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.Transaction = _dbContext.Database
                .CurrentTransaction?.GetDbTransaction();
            command.CommandText = includeInactive
                ? """
                  SELECT Id, CompanyId, ArabicName, IsActive
                  FROM dbo.HrJobPositions
                  ORDER BY ArabicName;
                  """
                : """
                  SELECT Id, CompanyId, ArabicName, IsActive
                  FROM dbo.HrJobPositions
                  WHERE IsActive = 1
                  ORDER BY ArabicName;
                  """;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(new PositionLookupRow
                {
                    Id = reader.GetInt32(0),
                    CompanyId = reader.GetInt32(1),
                    Name = reader.IsDBNull(2)
                        ? string.Empty
                        : reader.GetString(2),
                    IsActive = !reader.IsDBNull(3) &&
                               reader.GetBoolean(3)
                });
            }
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }

    private static int NormalizePageSize(int value)
    {
        return value switch
        {
            10 => 10,
            50 => 50,
            100 => 100,
            _ => 25
        };
    }

    private static string NormalizeStatusFilter(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "all" => "all",
            "inactive" => "inactive",
            _ => "active"
        };
    }

    private static string NormalizeSortBy(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "code" => "code",
            "branch" => "branch",
            "department" => "department",
            "hiredate" => "hiredate",
            "status" => "status",
            _ => "name"
        };
    }

    private sealed class PositionLookupRow
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}