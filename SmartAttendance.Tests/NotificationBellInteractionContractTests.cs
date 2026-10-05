using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Notifications;

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
    public void Employee_portal_loads_notification_token_stylesheets_before_rendering_the_panel()
    {
        var root = FindRepositoryRoot();
        var layout = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web/Pages/Shared/_EmployeePortalLayout.cshtml"));
        var css = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web/wwwroot/css/pages/default-fe42d97482.css"));

        Assert.Contains("~/css/zynora-migrated-color-tokens.css", layout, StringComparison.Ordinal);
        const string designTokens = "~/css/zynora-design-system.css";
        Assert.Contains(designTokens, layout, StringComparison.Ordinal);
        Assert.True(layout.IndexOf(designTokens, StringComparison.Ordinal)
            < layout.IndexOf("</head>", StringComparison.Ordinal));
        Assert.Contains("@media (max-width: 520px)", css, StringComparison.Ordinal);
    }



    [Theory]
    [InlineData("surface", "var(--zy-migrated-color-57c5aa9398, #121a2b)")]
    [InlineData("border", "var(--zy-migrated-color-b34d3e7911, rgba(255,255,255,.12))")]
    [InlineData("shadow", "var(--zy-migrated-color-fd9af3c8e7, rgba(0,0,0,.52))")]
    [InlineData("text", "var(--zy-migrated-color-3bd84bea55, #eaf6f8)")]
    [InlineData("divider", "var(--zy-migrated-color-c201d2dab2, rgba(255,255,255,.09))")]
    [InlineData("accent", "var(--zy-migrated-color-0f1cc78a41, #65d9e0)")]
    [InlineData("muted", "var(--zy-migrated-color-6d65c193c8, #9fb3c7)")]
    public void Employee_notification_palette_preserves_safe_defaults_in_shared_tokens(
        string role, string expectedValue)
    {
        var root = FindRepositoryRoot();
        var css = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web/wwwroot/css/pages/default-fe42d97482.css"));
        var tokens = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web/wwwroot/css/zynora-design-system.css"));
        var token = $"--color-employee-notification-{role}";
        Assert.Contains($"var({token})", css, StringComparison.Ordinal);
        Assert.Contains($"{token}: {expectedValue};", tokens, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_workflow_notifications_reuse_existing_employee_inbox()
    {
        var root = FindRepositoryRoot();
        var store = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web/Infrastructure/Notifications/EmployeeNotificationStore.cs"));
        var enums = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Domain/Enums/AnnouncementStudioEnums.cs"));

        Assert.Contains("RequestWorkflow = 6", enums, StringComparison.Ordinal);
        Assert.Contains("CreateRequestWorkflowAsync", store, StringComparison.Ordinal);
        Assert.Contains("new UserNotification", store, StringComparison.Ordinal);
        Assert.Contains("new UserNotificationRecipient", store, StringComparison.Ordinal);
        Assert.Contains("UserNotificationType.RequestWorkflow", store, StringComparison.Ordinal);
    }

    [Fact]
    public void Approval_workflow_notifications_use_exact_request_deep_links()
    {
        var root = FindRepositoryRoot();
        var engine = File.ReadAllText(Path.Combine(root,
            "SmartAttendance.Web/Infrastructure/Hrms/ApprovalWorkflowEngine.cs"));

        Assert.Contains("NotifyEmployeeRequestAsync(dbContext, requestId, \"تم إنشاء الطلب\"", engine, StringComparison.Ordinal);
        Assert.Contains("$\"/EmployeePortal?tab=requests&requestId={requestId}#employee-request-{requestId}\"", engine, StringComparison.Ordinal);
        Assert.Contains("$\"/Approvals?RequestId={requestId}\"", engine, StringComparison.Ordinal);
        Assert.Contains("\"طلب وصل إلى المدير المباشر\"", engine, StringComparison.Ordinal);
        Assert.Contains("\"طلب وصل إلى الموارد البشرية\"", engine, StringComparison.Ordinal);
        Assert.Contains("\"اعتماد المدير\"", engine, StringComparison.Ordinal);
        Assert.Contains("\"تم اعتماد الطلب\"", engine, StringComparison.Ordinal);
        Assert.Contains("\"تم رفض الطلب\"", engine, StringComparison.Ordinal);
        Assert.Contains("\"أُعيد الطلب للتعديل\"", engine, StringComparison.Ordinal);
        Assert.Contains("\"تم إلغاء الطلب\"", engine, StringComparison.Ordinal);
        Assert.Contains("ApprovalRequestUrl(requestId)", engine, StringComparison.Ordinal);
        Assert.Contains("N'/Approvals?RequestId=' + CAST(m.RequestId AS nvarchar(20))", engine, StringComparison.Ordinal);
        Assert.Contains("N'/Approvals?RequestId=' + CAST(RequestId AS nvarchar(20))", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("'/Approvals'", engine, StringComparison.Ordinal);
        Assert.DoesNotContain("'/SelfServices'", engine, StringComparison.Ordinal);
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

    [Fact]
    public async Task Employee_request_workflow_notification_round_trips_through_existing_inbox()
    {
        var connection = Environment.GetEnvironmentVariable("SMARTATTENDANCE_INTEGRATION_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;

        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
        await db.Database.OpenConnectionAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var employeeId = await db.Employees.AsNoTracking()
            .Where(employee => employee.EmployeeNo == "ZYN-1001")
            .Select(employee => employee.Id)
            .FirstOrDefaultAsync();
        Assert.True(employeeId > 0);

        var before = await EmployeeNotificationStore.GetForEmployeeAsync(db, employeeId, take: 50);
        const int requestId = 4;
        var url = $"/EmployeePortal?tab=requests&requestId={requestId}#employee-request-{requestId}";
        var title = "اختبار إشعار الطلب";
        var marker = "NotificationRoundTrip-" + Guid.NewGuid().ToString("N");

        var notificationId = await EmployeeNotificationStore.CreateRequestWorkflowAsync(
            db, employeeId, title, marker, url);
        Assert.True(notificationId > 0);

        var unread = await EmployeeNotificationStore.GetForEmployeeAsync(db, employeeId, take: 50);
        var created = Assert.Single(unread.Items, item => item.Id == notificationId);
        Assert.Equal(url, created.Url);
        Assert.False(created.IsRead);
        Assert.Equal(before.UnreadCount + 1, unread.UnreadCount);

        Assert.True(await EmployeeNotificationStore.MarkReadAsync(db, employeeId, notificationId) > 0);
        var read = await EmployeeNotificationStore.GetForEmployeeAsync(db, employeeId, take: 50);
        Assert.True(Assert.Single(read.Items, item => item.Id == notificationId).IsRead);
        Assert.Equal(before.UnreadCount, read.UnreadCount);

        await transaction.RollbackAsync();
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
