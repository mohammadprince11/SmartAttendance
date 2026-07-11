using SmartAttendance.Application.Setup.ViewModels;
using SmartAttendance.Domain.Enums;

namespace SmartAttendance.Application.Setup.Services;

public interface ISetupService
{
    Task<SystemSetupViewModel> GetSetupStatusAsync();

    Task<SetupActionResultViewModel> BulkAssignShiftAsync(BulkAssignShiftViewModel model);

    Task<CompanySetupViewModel?> GetCompanySetupAsync(int companyId);

    Task<SetupActionResultViewModel> UpdateCompanyProfileAsync(CompanySetupProfileViewModel model);

    Task<IReadOnlyList<PayrollCutoffPolicyViewModel>> GetPayrollCutoffPoliciesAsync(int companyId);

    Task<PayrollCutoffPolicyViewModel?> GetPayrollCutoffPolicyAsync(int companyId, int policyId);

    Task<SetupActionResultViewModel> SavePayrollCutoffPolicyAsync(
        PayrollCutoffPolicyViewModel model,
        IReadOnlyCollection<PayrollCutoffType> policyTypes);

    Task<SetupActionResultViewModel> DeletePayrollCutoffPolicyAsync(int companyId, int policyId);
}