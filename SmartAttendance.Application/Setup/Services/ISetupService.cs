using SmartAttendance.Application.Setup.ViewModels;

namespace SmartAttendance.Application.Setup.Services;

public interface ISetupService
{
    Task<SystemSetupViewModel> GetSetupStatusAsync();

    Task<SetupActionResultViewModel> BulkAssignShiftAsync(BulkAssignShiftViewModel model);

    Task<CompanySetupViewModel?> GetCompanySetupAsync(int companyId);

    Task<SetupActionResultViewModel> UpdateCompanyProfileAsync(CompanySetupProfileViewModel model);

    Task<SetupActionResultViewModel> SavePayrollSettingsAsync(CompanyPayrollSettingsViewModel model);

    Task<IReadOnlyList<PayrollCutoffPolicyViewModel>> GetPayrollCutoffPoliciesAsync(int companyId);

    Task<PayrollCutoffPolicyViewModel?> GetPayrollCutoffPolicyAsync(int companyId, int policyId);

    Task<SetupActionResultViewModel> SavePayrollCutoffPolicyAsync(PayrollCutoffPolicyViewModel model);

    Task<SetupActionResultViewModel> DeletePayrollCutoffPolicyAsync(int companyId, int policyId);
}