using AutoMapper;
using SmartAttendance.Application.Departments.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.Departments.Mappings;

public class DepartmentProfile : Profile
{
    public DepartmentProfile()
    {
        CreateMap<Department, DepartmentListViewModel>()
            .ForMember(dest => dest.BranchName,
                opt => opt.MapFrom(src => src.Branch != null ? src.Branch.Name : string.Empty));

        CreateMap<Department, DepartmentDetailsViewModel>()
            .ForMember(dest => dest.BranchName,
                opt => opt.MapFrom(src => src.Branch != null ? src.Branch.Name : string.Empty));

        CreateMap<Department, DepartmentEditViewModel>();

        CreateMap<DepartmentCreateViewModel, Department>();
        CreateMap<DepartmentEditViewModel, Department>();
    }
}