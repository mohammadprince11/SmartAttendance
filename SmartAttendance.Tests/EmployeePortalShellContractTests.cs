namespace SmartAttendance.Tests;

public sealed class EmployeePortalShellContractTests
{
    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void SharedLayout_UsesVersionedShellStylesheet_WithoutStaticInlineCss()
    {
        var layout = Read("SmartAttendance.Web", "Pages", "Shared", "_EmployeePortalLayout.cshtml");

        Assert.Contains("~/css/zynora-employee-portal-shell.css", layout);
        Assert.DoesNotContain("<style", layout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=\"", layout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("zy-ui-contract\" data-zy-preserve", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellStyles_RespectReducedMotion_AndLogicalDirections()
    {
        var css = Read("SmartAttendance.Web", "wwwroot", "css", "zynora-employee-portal-shell.css");

        Assert.Contains("prefers-reduced-motion", css);
        Assert.Contains("inset-inline", css);
        Assert.Contains("inset-block-end", css);
        Assert.DoesNotContain("left:", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("right:", css, StringComparison.OrdinalIgnoreCase);
    }
}
