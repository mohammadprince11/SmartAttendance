using AutoMapper;
using SmartAttendance.Application.Holidays.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.Holidays.Mappings;

public class HolidayProfile : Profile
{
    public HolidayProfile()
    {
        CreateMap<Holiday, HolidayListViewModel>();
        CreateMap<Holiday, HolidayDetailsViewModel>();
        CreateMap<Holiday, HolidayEditViewModel>();

        CreateMap<HolidayCreateViewModel, Holiday>();
        CreateMap<HolidayEditViewModel, Holiday>();
    }
}
