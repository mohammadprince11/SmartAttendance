using SmartAttendance.Application.ReportBuilder.ViewModels;

namespace SmartAttendance.Application.ReportBuilder.Services;

public interface IReportTemplateService
{
    Task<List<ReportTemplateViewModel>> GetAllAsync();

    Task<ReportTemplateViewModel?> GetByIdAsync(string id);

    Task<ReportTemplateViewModel> SaveAsync(ReportTemplateViewModel template);

    Task<bool> DeleteAsync(string id);
}
