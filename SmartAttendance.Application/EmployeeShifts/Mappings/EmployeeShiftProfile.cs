using AutoMapper;
using SmartAttendance.Application.EmployeeShifts.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.EmployeeShifts.Mappings;

public class EmployeeShiftProfile : Profile
{
    public EmployeeShiftProfile()
    {
        CreateMap<EmployeeShift, EmployeeShiftListViewModel>()
            .ForMember(dest => dest.EmployeeNo,
                opt => opt.MapFrom(src => src.Employee != null ? src.Employee.EmployeeNo : string.Empty))
            .ForMember(dest => dest.EmployeeName,
                opt => opt.MapFrom(src => src.Employee != null ? src.Employee.FullName : string.Empty))
            .ForMember(dest => dest.ShiftCode,
                opt => opt.MapFrom(src => src.Shift != null ? src.Shift.Code : string.Empty))
            .ForMember(dest => dest.ShiftName,
                opt => opt.MapFrom(src => src.Shift != null ? src.Shift.Name : string.Empty));

        CreateMap<EmployeeShift, EmployeeShiftDetailsViewModel>()
            .ForMember(dest => dest.EmployeeNo,
                opt => opt.MapFrom(src => src.Employee != null ? src.Employee.EmployeeNo : string.Empty))
            .ForMember(dest => dest.EmployeeName,
                opt => opt.MapFrom(src => src.Employee != null ? src.Employee.FullName : string.Empty))
            .ForMember(dest => dest.ShiftCode,
                opt => opt.MapFrom(src => src.Shift != null ? src.Shift.Code : string.Empty))
            .ForMember(dest => dest.ShiftName,
                opt => opt.MapFrom(src => src.Shift != null ? src.Shift.Name : string.Empty));

        CreateMap<EmployeeShift, EmployeeShiftEditViewModel>();

        CreateMap<EmployeeShiftCreateViewModel, EmployeeShift>();
        CreateMap<EmployeeShiftEditViewModel, EmployeeShift>();
    }
}
