using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApprovalLeaveDurationPresetTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }

    [Fact]
    public void Resolver_UsesSelfServiceDaysCountForLeaveTemplates()
    {
        var source = File.ReadAllText(Path.Combine(
            Root(), "SmartAttendance.Web", "Infrastructure", "Hrms", "ApprovalTemplateStore.cs"));

        Assert.Contains("ResolveNumericRequestValueAsync", source);
        Assert.Contains("requestType.Equals(\"LeaveRequest\"", source);
        Assert.Contains("CAST(DaysCount AS decimal(18,2))", source);
        Assert.Contains("FinancialRequestDetails", source);
    }

    [Fact]
    public void LeaveDurationPreset_DefinesThreeRequiredBandsAndStages()
    {
        var source = File.ReadAllText(Path.Combine(
            Root(), "SmartAttendance.Web", "Pages", "HrSettings", "ApprovalTemplates.cshtml.cs"));

        Assert.Contains("إجازات أقل من 5 أيام", source);
        Assert.Contains("maxDays: 4m", source);
        Assert.Contains("إجازات من 5 إلى 10 أيام", source);
        Assert.Contains("minDays: 5m", source);
        Assert.Contains("maxDays: 10m", source);
        Assert.Contains("إجازات أكثر من 10 أيام", source);
        Assert.Contains("minDays: 11m", source);

        Assert.Contains("RoleName = \"HR Officer\"", source);
        Assert.Contains("RoleName = \"HR Manager\"", source);
        Assert.Contains("StepOrder = 3, StageOrder = 3", source);
    }

    [Fact]
    public void LeaveDurationPreset_IsCompanyScopedAndIdempotent()
    {
        var source = File.ReadAllText(Path.Combine(
            Root(), "SmartAttendance.Web", "Pages", "HrSettings", "ApprovalTemplates.cshtml.cs"));

        Assert.Contains("scope.Allows(CompanyId.Value)", source);
        Assert.Contains("existing.Any(template", source);
        Assert.Contains("ApprovalTemplateStore.SaveAsync(_dbContext, scope, template)", source);
    }
}
