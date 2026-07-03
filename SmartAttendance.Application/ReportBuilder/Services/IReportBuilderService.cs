using SmartAttendance.Application.ReportBuilder.ViewModels;

namespace SmartAttendance.Application.ReportBuilder.Services;

public interface IReportBuilderService
{
    List<ReportBuilderColumnViewModel> GetColumns(string reportType);

    Task<List<ReportBuilderDropdownItemViewModel>> GetBranchesAsync();

    Task<List<ReportBuilderDropdownItemViewModel>> GetDepartmentsAsync(string? branchId);

    Task<List<ReportBuilderDropdownItemViewModel>> GetShiftsAsync();

    Task<ReportBuilderResultViewModel> BuildAsync(ReportBuilderRequestViewModel request);
}
