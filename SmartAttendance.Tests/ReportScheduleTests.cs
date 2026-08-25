using SmartAttendance.Web.Infrastructure.Notifications;
using SmartAttendance.Web.Infrastructure.Reports;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class ReportScheduleTests
{
    [Fact]
    public void DailyNextRun_IsStrictlyFutureAtConfiguredHour()
    {
        var now = new DateTime(2026, 8, 26, 7, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(2026, 8, 27, 6, 0, 0, DateTimeKind.Utc),
            ReportScheduleStore.Next(now, "Daily", 6, null));
    }

    [Fact]
    public void WeeklyNextRun_UsesConfiguredDayAndNeverReturnsPast()
    {
        var now = new DateTime(2026, 8, 26, 7, 30, 0, DateTimeKind.Utc); // Wednesday
        Assert.Equal(new DateTime(2026, 8, 30, 6, 0, 0, DateTimeKind.Utc),
            ReportScheduleStore.Next(now, "Weekly", 6, 0));
    }

    [Fact]
    public void SmtpMessage_PreservesCsvAttachment()
    {
        var options = new SmtpOptions { Enabled = true, Host = "smtp.example.com", FromAddress = "noreply@example.com" };
        using var mail = SmtpEmailSender.BuildMail(options,
            new EmailMessage("hr@example.com", "report", "body",
                new[] { new EmailAttachment("report.csv", "text/csv", new byte[] { 1, 2, 3 }) }));
        var attachment = Assert.Single(mail.Attachments.Cast<System.Net.Mail.Attachment>());
        Assert.Equal("report.csv", attachment.Name);
        Assert.Equal(3, attachment.ContentStream.Length);
    }

    [Fact]
    public void ScheduledReports_HaveTenantAndPermissionRechecks()
    {
        var root = FindRoot();
        var store = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Reports", "ReportScheduleStore.cs"));
        var dispatcher = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Infrastructure", "Reports", "ReportScheduleDispatcherService.cs"));
        Assert.Contains("scope.ToSqlPredicate(\"s.CompanyId\")", store, StringComparison.Ordinal);
        Assert.Contains("scope.Allows(companyId)", store, StringComparison.Ordinal);
        Assert.Contains("IsGrantedOrUnrestrictedAsync", dispatcher, StringComparison.Ordinal);
        Assert.Contains("Report permission was revoked", dispatcher, StringComparison.Ordinal);
        Assert.Contains("SqlDistributedLock.TryRunAsync", dispatcher, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx"))) directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
