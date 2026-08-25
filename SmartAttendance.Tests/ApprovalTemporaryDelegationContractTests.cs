using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalTemporaryDelegationContractTests
{
    [Fact]
    public void Delegation_IsCompanyScopedTimeBoundAndAuditedOnEveryDecision()
    {
        var root=FindRoot();
        var store=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","ApprovalDelegationStore.cs"));
        var engine=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","ApprovalWorkflowEngine.cs"));
        var migration=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","SqlSchemaMigrator.cs"));
        var page=File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Pages","HrSettings","ApprovalTemplates.cshtml.cs"));

        Assert.Contains("20260826-12-approval-temporary-delegations",migration,StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT FK_ApprovalDelegations_Company",migration,StringComparison.Ordinal);
        Assert.Contains("CHECK(EndsAt>StartsAt)",migration,StringComparison.Ordinal);
        Assert.Contains("Demand(scope, companyId)",store,StringComparison.Ordinal);
        Assert.Contains("sourceEmployee.CompanyId=d.CompanyId",store,StringComparison.Ordinal);
        Assert.Contains("targetEmployee.CompanyId=d.CompanyId",store,StringComparison.Ordinal);
        Assert.Contains("d.StartsAt<=SYSUTCDATETIME() AND d.EndsAt>SYSUTCDATETIME()",store,StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureAsync",store,StringComparison.Ordinal);
        Assert.Contains("ResolveAuthorizationAsync",engine,StringComparison.Ordinal);
        Assert.True(Count(engine,"@DelegatedFrom")>=9);
        Assert.True(Count(engine,"Notes, DelegatedFrom")>=2);
        Assert.Contains("ApprovalDelegationStore.CreateAsync",page,StringComparison.Ordinal);
        Assert.Contains("ApprovalDelegationStore.RevokeAsync",page,StringComparison.Ordinal);
    }

    private static int Count(string source,string value)
    {
        var count=0;
        for(var index=0;(index=source.IndexOf(value,index,StringComparison.Ordinal))>=0;index+=value.Length) count++;
        return count;
    }

    private static string FindRoot()
    {
        var directory=new DirectoryInfo(Directory.GetCurrentDirectory());
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"SmartAttendance.slnx"))) directory=directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
