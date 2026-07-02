using AutoMapper;
using SmartAttendance.Application.Shifts.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.Shifts.Mappings;

public class ShiftProfile : Profile
{
    public ShiftProfile()
    {
        CreateMap<Shift, ShiftListViewModel>();
        CreateMap<Shift, ShiftDetailsViewModel>();
        CreateMap<Shift, ShiftEditViewModel>();

        CreateMap<ShiftCreateViewModel, Shift>();
        CreateMap<ShiftEditViewModel, Shift>();
    }
}
