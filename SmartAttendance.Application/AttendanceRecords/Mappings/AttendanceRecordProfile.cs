using AutoMapper;
using SmartAttendance.Application.AttendanceRecords.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.AttendanceRecords.Mappings;

public class AttendanceRecordProfile : Profile
{
    public AttendanceRecordProfile()
    {
        CreateMap<AttendanceRecord, AttendanceRecordListViewModel>()
            .ForMember(dest => dest.EmployeeNo,
                opt => opt.MapFrom(src => src.Employee != null ? src.Employee.EmployeeNo : string.Empty))
            .ForMember(dest => dest.EmployeeName,
                opt => opt.MapFrom(src => src.Employee != null ? src.Employee.FullName : string.Empty))
            .ForMember(dest => dest.DeviceName,
                opt => opt.MapFrom(src => src.Device != null ? src.Device.Name : string.Empty));

        CreateMap<AttendanceRecord, AttendanceRecordDetailsViewModel>()
            .ForMember(dest => dest.EmployeeNo,
                opt => opt.MapFrom(src => src.Employee != null ? src.Employee.EmployeeNo : string.Empty))
            .ForMember(dest => dest.EmployeeName,
                opt => opt.MapFrom(src => src.Employee != null ? src.Employee.FullName : string.Empty))
            .ForMember(dest => dest.DeviceName,
                opt => opt.MapFrom(src => src.Device != null ? src.Device.Name : string.Empty));

        CreateMap<AttendanceRecord, AttendanceRecordEditViewModel>();

        CreateMap<AttendanceRecordCreateViewModel, AttendanceRecord>();
        CreateMap<AttendanceRecordEditViewModel, AttendanceRecord>();
    }
}
