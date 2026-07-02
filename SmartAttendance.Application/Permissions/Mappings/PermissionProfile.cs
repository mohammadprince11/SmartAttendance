using AutoMapper;
using SmartAttendance.Application.Permissions.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.Permissions.Mappings;

public class PermissionProfile : Profile
{
    public PermissionProfile()
    {
        CreateMap<Permission, PermissionListViewModel>();
        CreateMap<Permission, PermissionDetailsViewModel>();
        CreateMap<Permission, PermissionEditViewModel>();

        CreateMap<PermissionCreateViewModel, Permission>();
        CreateMap<PermissionEditViewModel, Permission>();
    }
}
