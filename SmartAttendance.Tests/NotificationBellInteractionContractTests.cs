namespace SmartAttendance.Tests;

public sealed class NotificationBellInteractionContractTests
{
    [Theory]
    [InlineData("SmartAttendance.Web/Pages/Shared/Components/NotificationBell/Default.cshtml", "zyBellTrigger", "zyBellPanel")]
    [InlineData("SmartAttendance.Web/Pages/Shared/Components/EmployeeNotificationBell/Default.cshtml", "empBellTrigger", "empBellPanel")]
    public void Notification_bell_uses_a_floating_button_panel_instead_of_details(
        string relativePath,
        string triggerId,
        string panelId)
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(root, relativePath));

        Assert.DoesNotContain("<details class=\"zy-bell\"", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<details class=\"emp-bell\"", markup, StringComparison.Ordinal);
        Assert.Contains($"id=\"{triggerId}\"", markup, StringComparison.Ordinal);
        Assert.Contains($"aria-controls=\"{panelId}\"", markup, StringComparison.Ordinal);
        Assert.Contains($"id=\"{panelId}\"", markup, StringComparison.Ordinal);
        Assert.Contains("positionPanel", markup, StringComparison.Ordinal);
        Assert.Contains("panel.hidden", markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SmartAttendance.Web/wwwroot/css/pages/default-6a5461e23c.css", ".zy-bell-panel")]
    [InlineData("SmartAttendance.Web/wwwroot/css/pages/default-fe42d97482.css", ".emp-bell-panel")]
    public void Notification_panel_is_fixed_and_cannot_push_page_layout(string relativePath, string selector)
    {
        var root = FindRepositoryRoot();
        var css = File.ReadAllText(Path.Combine(root, relativePath));

        Assert.Contains(selector, css, StringComparison.Ordinal);
        Assert.Contains("position: fixed", css, StringComparison.Ordinal);
        Assert.Contains("calc(100vw - 24px)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Employee_portal_loads_notification_color_tokens_and_panel_uses_tokens()
    {
        var root = FindRepositoryRoot();
        var layout = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web/Pages/Shared/_EmployeePortalLayout.cshtml"));
        var css = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web/wwwroot/css/pages/default-fe42d97482.css"));

        Assert.Contains("~/css/zynora-migrated-color-tokens.css", layout, StringComparison.Ordinal);
        Assert.Contains("var(--zy-migrated-color-57c5aa9398)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 520px)", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Employee_notification_zero_badge_is_really_hidden()
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web/Pages/Shared/Components/EmployeeNotificationBell/Default.cshtml"));
        var css = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web/wwwroot/css/pages/default-fe42d97482.css"));

        Assert.Contains("hidden=\"@(unread > 0 ? null : \"hidden\")\"", markup, StringComparison.Ordinal);
        Assert.Contains("badge.hidden = true", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("badge.style.display", markup, StringComparison.Ordinal);
        Assert.Contains(".emp-bell-badge[hidden]", css, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "SmartAttendance.Web")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
