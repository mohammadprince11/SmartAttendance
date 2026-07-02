using AutoMapper;
using SmartAttendance.Application.Common.Interfaces.Repositories;
using SmartAttendance.Application.SystemUsers.Services;
using SmartAttendance.Application.SystemUsers.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Infrastructure.Services;

public class SystemUserService : ISystemUserService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public SystemUserService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<IEnumerable<SystemUserListViewModel>> GetAllAsync(string? searchTerm = null)
    {
        var users = await _unitOfWork.SystemUsers.GetAllAsync();

        var result = _mapper.Map<IEnumerable<SystemUserListViewModel>>(users);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            result = result.Where(x =>
                x.FullName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.UserName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.Email != null && x.Email.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                x.Role.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                (x.Notes != null && x.Notes.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)));
        }

        return result
            .OrderBy(x => x.FullName)
            .ToList();
    }

    public async Task<SystemUserDetailsViewModel?> GetByIdAsync(int id)
    {
        var user = await _unitOfWork.SystemUsers.GetByIdAsync(id);

        if (user == null)
            return null;

        return _mapper.Map<SystemUserDetailsViewModel>(user);
    }

    public async Task<SystemUserEditViewModel?> GetEditByIdAsync(int id)
    {
        var user = await _unitOfWork.SystemUsers.GetByIdAsync(id);

        if (user == null)
            return null;

        return _mapper.Map<SystemUserEditViewModel>(user);
    }

    public async Task<bool> CreateAsync(SystemUserCreateViewModel model)
    {
        var users = await _unitOfWork.SystemUsers.GetAllAsync();

        var isDuplicateUserName = users.Any(x =>
            string.Equals(x.UserName.Trim(), model.UserName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (isDuplicateUserName)
            return false;

        var user = _mapper.Map<SystemUser>(model);

        user.UserName = user.UserName.Trim();
        user.FullName = user.FullName.Trim();
        user.Email = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email.Trim();

        await _unitOfWork.SystemUsers.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> UpdateAsync(SystemUserEditViewModel model)
    {
        var user = await _unitOfWork.SystemUsers.GetByIdAsync(model.Id);

        if (user == null)
            return false;

        var users = await _unitOfWork.SystemUsers.GetAllAsync();

        var isDuplicateUserName = users.Any(x =>
            x.Id != model.Id &&
            string.Equals(x.UserName.Trim(), model.UserName.Trim(), StringComparison.OrdinalIgnoreCase));

        if (isDuplicateUserName)
            return false;

        user.FullName = model.FullName.Trim();
        user.UserName = model.UserName.Trim();
        user.Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim();
        user.Role = model.Role;
        user.IsActive = model.IsActive;
        user.Notes = model.Notes;

        _unitOfWork.SystemUsers.Update(user);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var user = await _unitOfWork.SystemUsers.GetByIdAsync(id);

        if (user == null)
            return false;

        _unitOfWork.SystemUsers.Delete(user);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }
}
