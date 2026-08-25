using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حواجز انحدار لبيانات التعويض: لا يكفي إخفاؤها في المتصفح، ولا تكفي صلاحية
/// تعديل ملف الموظف العامة لتغيير العلاوات.
/// </summary>
public sealed class EmployeeCompensationSecurityContractTests
{
    private static string Web(params string[] parts)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "SmartAttendance.Web", Path.Combine(parts));
    }

    [Fact]
    public void Profile_DoesNotRenderCompensationWithHtmlHiddenAttribute()
    {
        var page = File.ReadAllText(Web("Pages", "Employees", "Profile.cshtml"));

        Assert.Contains("@if (Model.CanViewSalary)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden=\"@(!Model.CanViewSalary)\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Profile_DoesNotLoadFinancialRowsWithoutViewAuthorization()
    {
        var model = File.ReadAllText(Web("Pages", "Employees", "Profile.Panels.cshtml.cs"));
        var gate = model.IndexOf("if (CanViewSalary)", StringComparison.Ordinal);
        var financialQuery = model.IndexOf("EmployeeFinancialInfos.AsNoTracking()", StringComparison.Ordinal);
        var allowanceQuery = model.IndexOf("EmployeeAllowances.AsNoTracking()", StringComparison.Ordinal);

        Assert.True(gate >= 0 && financialQuery > gate && allowanceQuery > gate);
    }

    [Theory]
    [InlineData("OnPostSaveAllowanceAsync")]
    [InlineData("OnPostDeleteAllowanceAsync")]
    public void AllowanceMutations_RequireEditCompensation(string handler)
    {
        var model = File.ReadAllText(Web("Pages", "Employees", "Profile.Panels.cshtml.cs"));
        var start = model.IndexOf(handler, StringComparison.Ordinal);
        Assert.True(start >= 0);
        var body = model.Substring(start, Math.Min(500, model.Length - start));

        Assert.Contains("PeoplePermissionCodes.EditCompensation", body, StringComparison.Ordinal);
        Assert.Contains("return Forbid()", body, StringComparison.Ordinal);
    }

    [Fact]
    public void FinancialPage_HonoursSensitiveSalaryGrantForViewing()
    {
        var model = File.ReadAllText(Web("Pages", "Employees", "FinancialInfo.cshtml.cs"));

        Assert.Contains("AccessRoleStore.HasSensitiveFieldAsync", model, StringComparison.Ordinal);
        Assert.Contains("SensitiveFieldCatalog.Salary", model, StringComparison.Ordinal);
    }
}
