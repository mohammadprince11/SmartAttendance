using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalValueFieldConditionContractTests
{
    [Fact]
    public void Resolver_UsesFrozenRequestAmountAndChangedFieldConditions()
    {
        var root=FindRoot();
        var store=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","ApprovalTemplateStore.cs"));
        var engine=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","ApprovalWorkflowEngine.cs"));
        var migration=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","SqlSchemaMigrator.cs"));
        Assert.Contains("20260826-15-approval-value-field-conditions",migration,StringComparison.Ordinal);
        Assert.Contains("FinancialRequestDetails",store,StringComparison.Ordinal);
        Assert.Contains("DataChangeRequestFields",store,StringComparison.Ordinal);
        Assert.Contains("amountOk",store,StringComparison.Ordinal);
        Assert.Contains("changedFieldOk",store,StringComparison.Ordinal);
        Assert.Contains("employeeInfo.WorkType,requestId",engine,StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory=new DirectoryInfo(Directory.GetCurrentDirectory());
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"SmartAttendance.slnx"))) directory=directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
