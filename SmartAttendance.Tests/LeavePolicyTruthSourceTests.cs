using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

public class LeavePolicyTruthSourceTests
{
    private static string WebRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SmartAttendance.Web"));

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { WebRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void EssRequestSubmission_UsesCompanyLeavePolicyEngine()
    {
        var source = Read("Pages", "EmployeePortal", "Index.cshtml.cs");

        Assert.Contains("CompanyLeavePolicyStore.ValidateRequestAsync", source);
        Assert.DoesNotContain("typeLabel == \"إجازة سنوية\"", source);
    }

    [Fact]
    public void EssRequestSubmission_PersistsRequestTypeIdentity()
    {
        var source = Read("Pages", "EmployeePortal", "Index.cshtml.cs");

        Assert.Contains("(EmployeeId, RequestTypeId, RequestType, CreatedAt", source);
        Assert.Contains("@RequestTypeId, @RequestType", source);
        Assert.Contains("typeDef?.Id", source);
    }

    [Fact]
    public void EssBalanceDisplay_UsesCompanyPolicySnapshots()
    {
        var pageModel = Read("Pages", "EmployeePortal", "Index.cshtml.cs");
        var razor = Read("Pages", "EmployeePortal", "Index.cshtml");

        Assert.Contains("CompanyLeavePolicyStore.GetBalanceSnapshotsAsync", pageModel);
        Assert.DoesNotContain("LeaveBalanceCalculator.ForEmployeeAsync", pageModel);
        Assert.Contains("BalanceSourceRequestTypeId", razor);
        Assert.Contains("SourceRequestTypeId", razor);
    }

    [Fact]
    public void MeApiBalance_UsesCompanyPolicySnapshots()
    {
        var source = Read("Controllers", "Api", "MeController.cs");

        Assert.Contains("CompanyLeavePolicyStore.GetBalanceSnapshotsAsync", source);
        Assert.DoesNotContain("LeaveBalanceCalculator.ForEmployeeAsync", source);
        Assert.Contains("requestTypeId = b.SourceRequestTypeId", source);
        Assert.Contains("unit = b.Unit", source);
    }

    [Fact]
    public void PayrollLeaveEncashment_UsesCompanyPolicySnapshots()
    {
        var policy = Read("Infrastructure", "Hrms", "LeaveEncashmentPolicy.cs");
        var page = Read("Pages", "Payroll", "LeaveEncashment.cshtml.cs");

        Assert.Contains("CompanyLeavePolicyStore.GetBalanceSnapshotsAsync", policy);
        Assert.DoesNotContain("LeaveBalanceCalculator.ForEmployeeAsync", policy);
        Assert.Contains("AnnualSourceRequestTypeIdAsync", policy);

        Assert.Contains("CompanyLeavePolicyStore.GetBalanceSnapshotsAsync", page);
        Assert.DoesNotContain("LeaveBalanceCalculator.ForEmployeeAsync", page);
        Assert.Contains("AvailableAnnualDaysAsync", page);
    }
}
