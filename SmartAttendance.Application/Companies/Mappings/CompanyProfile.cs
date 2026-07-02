using AutoMapper;
using SmartAttendance.Application.Companies.ViewModels;
using SmartAttendance.Domain.Entities;

namespace SmartAttendance.Application.Companies.Mappings;

public class CompanyProfile : Profile
{
    public CompanyProfile()
    {
        CreateMap<Company, CompanyDetailsViewModel>();
        CreateMap<Company, CompanyListViewModel>();
        CreateMap<Company, CompanyEditViewModel>();

        CreateMap<CompanyCreateViewModel, Company>();
        CreateMap<CompanyEditViewModel, Company>();
    }
}