using SmartAttendance.Application.Setup.ViewModels;

namespace SmartAttendance.Application.Setup.Services;

public interface ISetupService
{
    Task<SystemSetupViewModel> GetSetupStatusAsync();

    Task<SetupActionResultViewModel> BulkAssignShiftAsync(BulkAssignShiftViewModel model);
}
