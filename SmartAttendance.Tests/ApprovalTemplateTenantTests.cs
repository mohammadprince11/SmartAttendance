using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalTemplateTenantTests
{
    [Fact]
    public void ApprovalTemplateConfiguration_IsCompanyScopedOnEveryMutation()
    {
        var root = FindRoot();
        var store = File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","ApprovalTemplateStore.cs"));
        var page = File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Pages","HrSettings","ApprovalTemplates.cshtml.cs"));
        var engine = File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","ApprovalWorkflowEngine.cs"));
        var migration = File.ReadAllText(Path.Combine(root,"SmartAttendance.Web","Infrastructure","Hrms","SqlSchemaMigrator.cs"));
        Assert.Contains("20260826-11-approval-template-company-scope", migration, StringComparison.Ordinal);
        Assert.Contains("WHERE CompanyId=@CompanyId AND RequestType=@Type", store, StringComparison.Ordinal);
        Assert.Contains("WHERE Id=@Id AND CompanyId=@CompanyId", store, StringComparison.Ordinal);
        Assert.Contains("scope.Allows(template.CompanyId)", store, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync", store, StringComparison.Ordinal);
        Assert.Contains("e.Id == employeeId && e.CompanyId == CompanyId", page, StringComparison.Ordinal);
        Assert.Contains("employeeInfo.CompanyId", engine, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory=new DirectoryInfo(Directory.GetCurrentDirectory());
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"SmartAttendance.slnx"))) directory=directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
