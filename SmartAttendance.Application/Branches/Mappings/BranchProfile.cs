using AutoMapper;
using SmartAttendance.Application.Branches.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.Branches.Mappings;

public class BranchProfile : Profile
{
    public BranchProfile()
    {
        CreateMap<Branch, BranchListViewModel>()
            .ForMember(dest => dest.CompanyName,
                opt => opt.MapFrom(src => src.Company != null ? src.Company.Name : string.Empty));

        CreateMap<Branch, BranchDetailsViewModel>()
            .ForMember(dest => dest.CompanyName,
                opt => opt.MapFrom(src => src.Company != null ? src.Company.Name : string.Empty));

        CreateMap<Branch, BranchEditViewModel>();

        CreateMap<BranchCreateViewModel, Branch>();
        CreateMap<BranchEditViewModel, Branch>();
    }
}