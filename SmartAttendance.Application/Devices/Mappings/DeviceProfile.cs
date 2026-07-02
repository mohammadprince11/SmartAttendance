using AutoMapper;
using SmartAttendance.Application.Devices.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.Devices.Mappings;

public class DeviceProfile : Profile
{
    public DeviceProfile()
    {
        CreateMap<Device, DeviceListViewModel>()
            .ForMember(dest => dest.BranchName,
                opt => opt.MapFrom(src => src.Branch != null ? src.Branch.Name : string.Empty));

        CreateMap<Device, DeviceDetailsViewModel>()
            .ForMember(dest => dest.BranchName,
                opt => opt.MapFrom(src => src.Branch != null ? src.Branch.Name : string.Empty));

        CreateMap<Device, DeviceEditViewModel>();

        CreateMap<DeviceCreateViewModel, Device>();
        CreateMap<DeviceEditViewModel, Device>();
    }
}
