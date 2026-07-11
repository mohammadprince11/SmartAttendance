namespace SmartAttendance.Application.Setup.ViewModels;

public class CompanySetupViewModel
{
    public int CompanyId { get; set; }

    public string CompanyCode { get; set; } = string.Empty;

    public CompanySetupProfileViewModel Profile { get; set; } = new();

    public List<PayrollCutoffPolicyViewModel> CutoffPolicies { get; set; } = new();
}