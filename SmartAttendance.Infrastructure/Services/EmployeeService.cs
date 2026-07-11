using System.Data;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Application.Employees.Services;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Persistence;

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
                     StringComparison.OrdinalIgnoreCase)) ||
                (x.Email != null &&
                 x.Email.Contains(
                     searchTerm,
                     StringComparison.OrdinalIgnoreCase)) ||
                (x.Position != null &&
                 x.Position.Contains(
                     searchTerm,
                     StringComparison.OrdinalIgnoreCase)) ||
                x.DepartmentCode.Contains(
                    searchTerm,
                    StringComparison.OrdinalIgnoreCase) ||
                x.DepartmentName.Contains(
                    searchTerm,
                    StringComparison.OrdinalIgnoreCase) ||
                x.BranchName.Contains(
                    searchTerm,
                    StringComparison.OrdinalIgnoreCase));
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

        var employees = await _unitOfWork.Employees.GetAllAsync();

        var duplicateEmployeeNo = employees.Any(x =>
            x.Id != model.Id &&
            x.EmployeeNo.Equals(
                model.EmployeeNo,
                StringComparison.OrdinalIgnoreCase));

        if (duplicateEmployeeNo)
        {
            return false;
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
        var employees = await _unitOfWork.Employees.GetAllAsync();

        return employees.Any(x =>
            x.EmployeeNo.Equals(
                employeeNo,
                StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IEnumerable<DepartmentListViewModel>>
        GetDepartmentsForDropdownAsync()
    {
        var departments = await _unitOfWork.Departments.GetAllAsync();
        var companies = await _unitOfWork.Companies.GetAllAsync();

        var companyLookup = companies
            .Where(x => x.IsActive)
            .ToDictionary(x => x.Id, x => x.Name);

        return departments
            .Where(x =>
                x.IsActive &&
                companyLookup.ContainsKey(x.CompanyId))
            .OrderBy(x => companyLookup[x.CompanyId])
            .ThenBy(x => x.Name)
            .Select(x => new DepartmentListViewModel
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                IsActive = x.IsActive,
                CompanyId = x.CompanyId,
                CompanyName = companyLookup[x.CompanyId],
                BranchId = 0,
                BranchName = string.Empty
            })
            .ToList();
    }

    public async Task<IEnumerable<BranchListViewModel>>
        GetBranchesForDropdownAsync()
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

    private sealed class PositionLookupRow
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}
