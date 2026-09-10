using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeeLifecycleApprovalContractTests
{
    [Theory]
    [InlineData("Onboarding")]
    [InlineData("Offboarding")]
    public void ApprovalCatalog_ContainsLifecycleTypes(string key)
    {
        Assert.Contains(
            ApprovalTemplateStore.RequestTypes,
            type => string.Equals(type.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Onboarding", "Onboarding")]
    [InlineData("تهيئة موظف", "Onboarding")]
    [InlineData("Offboarding", "Offboarding")]
    [InlineData("إنهاء خدمة", "Offboarding")]
    public void LifecycleAliases_ResolveToApprovalTemplateKeys(
        string input,
        string expected)
    {
        Assert.Equal(expected, ApprovalWorkflowEngine.ResolveRequestTypeKey(input));
    }

    [Fact]
    public void LifecycleStore_SnapshotsTasks_AndUsesGenericApprovalEngine()
    {
        var source = Read(
            "SmartAttendance.Web",
            "Infrastructure",
            "Hrms",
            "EmployeeLifecycleApprovalStore.cs");

        Assert.Contains("EmployeeLifecycleRequests", source, StringComparison.Ordinal);
        Assert.Contains("EmployeeLifecycleRequestTasks", source, StringComparison.Ordinal);
        Assert.Contains("ApprovalWorkflowEngine.StartAsync", source, StringComparison.Ordinal);
        Assert.Contains("ApplyIfLifecycleAsync", source, StringComparison.Ordinal);
        Assert.Contains("AppliedAt IS NULL", source, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO EmployeeTasks", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeeTasks_Launch_SubmitsApprovalInsteadOfCreatingTasksImmediately()
    {
        var source = Read(
            "SmartAttendance.Web",
            "Pages",
            "EmployeeTasks",
            "Index.cshtml.cs");

        var start = source.IndexOf(
            "OnPostLaunchAsync",
            StringComparison.Ordinal);

        var end = source.IndexOf(
            "// ---- Task actions ----",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);

        var launch = source[start..end];

        Assert.Contains(
            "EmployeeLifecycleApprovalStore.SubmitAsync",
            launch,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "_dbContext.EmployeeTasks.Add",
            launch,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FinalApproval_AppliesLifecycleEffect()
    {
        var approvals = Read(
            "SmartAttendance.Web",
            "Pages",
            "Approvals",
            "Index.cshtml.cs");

        Assert.Contains(
            "EmployeeLifecycleApprovalStore.ApplyIfLifecycleAsync",
            approvals,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeeTasks_View_ExposesApprovalCycle()
    {
        var view = Read(
            "SmartAttendance.Web",
            "Pages",
            "EmployeeTasks",
            "Index.cshtml");

        Assert.Contains(
            "data-hrms-tab=\"approval-cycle\"",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "ResubmitLifecycle",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "/HrSettings/ApprovalTemplates",
            view,
            StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { FindRoot() }.Concat(parts).ToArray()));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find SmartAttendance.slnx.");
    }
}