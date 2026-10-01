using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class TenantCodeLoginContractTests
{
    [Theory]
    [InlineData("0001", true)]
    [InlineData("9876", true)]
    [InlineData("123", false)]
    [InlineData("12345", false)]
    [InlineData("12A4", false)]
    [InlineData("١٢٣٤", false)]
    [InlineData(null, false)]
    public void TenantCode_IsExactlyFourAsciiDigits(string? code, bool expected)
    {
        Assert.Equal(expected, TenantContext.IsValidCode(code));
    }

    [Fact]
    public void LoginAndSchema_AreTenantScoped()
    {
        var root = RepoRoot();
        var loginPage = Read(root, "SmartAttendance.Web", "Pages", "Account", "Login.cshtml");
        var loginStore = Read(root, "SmartAttendance.Web", "Infrastructure", "Security", "LoginDatabase.cs");
        var migration = Read(root, "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs");
        var tokenStore = Read(root, "SmartAttendance.Web", "Infrastructure", "Api", "ApiTokenStore.cs");
        var companyScope = Read(root, "SmartAttendance.Web", "Infrastructure", "Security", "CompanyScope.cs");

        Assert.Contains("asp-for=\"TenantCode\"", loginPage, StringComparison.Ordinal);
        Assert.Contains("u.TenantId = @TenantId AND u.Username = @Username", loginStore, StringComparison.Ordinal);
        Assert.Contains("20261001-01-tenant-code-login", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE dbo.Tenants", migration, StringComparison.Ordinal);
        Assert.Contains("IX_AppLoginUsers_TenantId_Username", migration, StringComparison.Ordinal);
        Assert.Contains("TenantId", tokenStore, StringComparison.Ordinal);
        Assert.Contains("company.TenantId == tenantId.Value", companyScope, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
