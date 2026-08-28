using Xunit;

namespace SmartAttendance.Tests;

public sealed class BankTemplateCompanyScopeTests
{
    [Fact]
    public void BankTemplates_AreMigratedScopedAndBoundToThePayrollRunCompany()
    {
        var root = FindRoot();
        var store = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "BankFileTemplateStore.cs"));
        var migration = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs"));
        var detail = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Payroll", "RunDetail.cshtml.cs"));

        Assert.Contains("20260825-01-bank-template-company-scope", migration, StringComparison.Ordinal);
        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope", store, StringComparison.Ordinal);
        Assert.Contains("CanUseCompany(scope", store, StringComparison.Ordinal);
        Assert.DoesNotContain("N'الرافدين (نموذج)'", store, StringComparison.Ordinal);
        Assert.Contains("run.CompanyId", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("BankFileTemplateStore.ActiveAsync(_db);", detail, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
