using AutoMapper;
using SmartAttendance.Application.LeaveRequests.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.LeaveRequests.Mappings;

public class LeaveRequestProfile : Profile
{
    public LeaveRequestProfile()
    {
        CreateMap<LeaveRequest, LeaveRequestListViewModel>()
            .ForMember(dest => dest.EmployeeNo,
                opt => opt.MapFrom(src => src.Employee != null ? src.Employee.EmployeeNo : string.Empty))
            .ForMember(dest => dest.EmployeeName,
                opt => opt.MapFrom(src => src.Employee != null ? src.Employee.FullName : string.Empty))
            .ForMember(dest => dest.TotalDays,
                opt => opt.MapFrom(src => src.ToDate.DayNumber - src.FromDate.DayNumber + 1));

        CreateMap<LeaveRequest, LeaveRequestDetailsViewModel>()
            .ForMember(dest => dest.EmployeeNo,
                opt => opt.MapFrom(src => src.Employee != null ? src.Employee.EmployeeNo : string.Empty))
            .ForMember(dest => dest.EmployeeName,
                opt => opt.MapFrom(src => src.Employee != null ? src.Employee.FullName : string.Empty))
            .ForMember(dest => dest.TotalDays,
                opt => opt.MapFrom(src => src.ToDate.DayNumber - src.FromDate.DayNumber + 1));

        CreateMap<LeaveRequest, LeaveRequestEditViewModel>();

        CreateMap<LeaveRequestCreateViewModel, LeaveRequest>();
        CreateMap<LeaveRequestEditViewModel, LeaveRequest>();
    }
}
