using AutoMapper;
using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.Employees.Mappings;

public class EmployeeProfile : Profile
{
    public EmployeeProfile()
    {
        CreateMap<Employee, EmployeeListViewModel>()
            .ForMember(dest => dest.DepartmentName,
                opt => opt.MapFrom(src => src.Department != null ? src.Department.Name : string.Empty))
            .ForMember(dest => dest.BranchName,
                opt => opt.MapFrom(src => src.Department != null && src.Department.Branch != null ? src.Department.Branch.Name : string.Empty));

        CreateMap<Employee, EmployeeDetailsViewModel>()
            .ForMember(dest => dest.DepartmentName,
                opt => opt.MapFrom(src => src.Department != null ? src.Department.Name : string.Empty))
            .ForMember(dest => dest.BranchName,
                opt => opt.MapFrom(src => src.Department != null && src.Department.Branch != null ? src.Department.Branch.Name : string.Empty));

        CreateMap<Employee, EmployeeEditViewModel>();

        CreateMap<EmployeeCreateViewModel, Employee>();
        CreateMap<EmployeeEditViewModel, Employee>();
    }
}
