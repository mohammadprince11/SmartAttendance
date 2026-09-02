using System.Text.RegularExpressions;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class UserMenuContractTests
{
    [Fact]
    public void AccountMenu_ExposesSixPermissionAwareActionsWithRealDestinations()
    {
        var layout = ReadWeb("Pages", "Shared", "_Layout.cshtml");

        Assert.Contains("data-zy-user-menu-trigger", layout, StringComparison.Ordinal);
        Assert.Contains("data-zy-user-name-trigger", layout, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@T[\"فتح قائمة حساب {0}\", userMenuDisplayName]\"", layout, StringComparison.Ordinal);
        Assert.Contains("userMenuInitials", layout, StringComparison.Ordinal);
        Assert.Contains("aria-haspopup=\"menu\"", layout, StringComparison.Ordinal);
        Assert.Contains("role=\"menu\"", layout, StringComparison.Ordinal);
        Assert.Contains("data-zy-language-open", layout, StringComparison.Ordinal);
        Assert.Contains("/Setup#company-currency", layout, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/Account/ChangePassword\"", layout, StringComparison.Ordinal);
        Assert.Contains("asp-fragment=\"employee-signature\"", layout, StringComparison.Ordinal);
        Assert.Contains("/EmployeePortal/Biometric", layout, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/Account/Logout\"", layout, StringComparison.Ordinal);
        Assert.Contains("aria-disabled=\"true\"", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountMenu_KeyboardAndDismissalContract_IsImplementedWithoutMarkupInjection()
    {
        var script = ReadWeb("wwwroot", "js", "zynora-user-menu.js");
        var layout = ReadWeb("Pages", "Shared", "_Layout.cshtml");

        foreach (var key in new[] { "ArrowDown", "ArrowUp", "Home", "End", "Escape" })
            Assert.Contains(key, script, StringComparison.Ordinal);

        Assert.Contains("pointerdown", script, StringComparison.Ordinal);
        Assert.Contains("aria-expanded", script, StringComparison.Ordinal);
        Assert.Contains("data-zy-language-option", layout, StringComparison.Ordinal);
        Assert.Contains("asp-page=\"/Culture/Set\"", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("ZY.Language", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccountMenu_UsesDesignTokensAndLogicalPositioning()
    {
        var css = ReadWeb("wwwroot", "css", "zynora-user-menu.css");
        var rules = Regex.Replace(css, @"/\*.*?\*/", " ", RegexOptions.Singleline);

        Assert.Contains("--zy-surface", rules, StringComparison.Ordinal);
        Assert.Contains("inset-inline-end", rules, StringComparison.Ordinal);
        Assert.Contains("border-block-end", rules, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"#[0-9a-f]{3,8}\b|rgba?\(", RegexOptions.IgnoreCase), rules);
        Assert.DoesNotMatch(new Regex(@"(?<![-\w])(left|right)\s*:", RegexOptions.IgnoreCase), rules);
    }

    [Fact]
    public void CurrencyAndSignatureDestinations_HaveStableAnchors()
    {
        var setup = ReadWeb("Pages", "Setup", "Index.cshtml");
        var employeeEdit = ReadWeb("Pages", "Employees", "Edit.cshtml");

        Assert.Contains("id=\"company-currency\"", setup, StringComparison.Ordinal);
        Assert.Contains("id=\"employee-signature\"", employeeEdit, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeeEdit_DefersEnglishNamesAndUsesTheDesignedSignaturePicker()
    {
        var employeeEdit = ReadWeb("Pages", "Employees", "Edit.cshtml");
        var employeeEditModel = ReadWeb("Pages", "Employees", "Edit.cshtml.cs");

        foreach (var property in new[] { "FirstNameEn", "SecondNameEn", "ThirdNameEn", "LastNameEn" })
        {
            Assert.DoesNotContain($"asp-for=\"Employee.{property}\"", employeeEdit, StringComparison.Ordinal);
            Assert.Contains($"Employee.{property} = current.{property};", employeeEditModel, StringComparison.Ordinal);
        }

        Assert.Contains("class=\"nxr-edit-signature-card\"", employeeEdit, StringComparison.Ordinal);
        Assert.Contains("data-signature-file-name", employeeEdit, StringComparison.Ordinal);
        Assert.Contains("RequiredFieldKeys.ExceptWith(DeferredTranslatedNameKeys)", employeeEditModel, StringComparison.Ordinal);
    }

    private static string ReadWeb(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "SmartAttendance.Web" }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
