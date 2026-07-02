using AutoMapper;
using SmartAttendance.Application.SystemUsers.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.SystemUsers.Mappings;

public class SystemUserProfile : Profile
{
    public SystemUserProfile()
    {
        CreateMap<SystemUser, SystemUserListViewModel>();
        CreateMap<SystemUser, SystemUserDetailsViewModel>();
        CreateMap<SystemUser, SystemUserEditViewModel>();

        CreateMap<SystemUserCreateViewModel, SystemUser>();
        CreateMap<SystemUserEditViewModel, SystemUser>();
    }
}
