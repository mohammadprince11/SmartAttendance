using Xunit;

namespace SmartAttendance.Tests;

public sealed class EngagementUnifiedHandlerCleanupContractTests
{
    [Theory]
    [InlineData("Announcements.cshtml")]
    [InlineData("Announcements.cshtml.cs")]
    [InlineData("Polls.cshtml")]
    [InlineData("Polls.cshtml.cs")]
    [InlineData("Feedback.cshtml")]
    [InlineData("Feedback.cshtml.cs")]
    public void LegacyEngagementPhysicalPages_AreDeleted(string fileName)
    {
        Assert.False(File.Exists(Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "Pages",
            "Engagement",
            fileName)));
    }

    [Fact]
    public void UnifiedIndexOwnsAllWriteHandlers()
    {
        var handlers = File.ReadAllText(Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "Pages",
            "Engagement",
            "Index.Handlers.cs"));

        Assert.Contains("OnPostAnnouncementCreateAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("OnPostAnnouncementArchiveAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("OnPostAnnouncementToggleAsync", handlers, StringComparison.Ordinal);

        Assert.Contains("OnPostPollCreateAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("OnPostPollToggleAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("OnPostPollDeleteAsync", handlers, StringComparison.Ordinal);

        Assert.Contains("OnPostFeedbackReplyAsync", handlers, StringComparison.Ordinal);
        Assert.Contains("OnPostFeedbackCloseAsync", handlers, StringComparison.Ordinal);
    }

    [Fact]
    public void UnifiedViewPostsOnlyToUnifiedIndex()
    {
        var view = File.ReadAllText(Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "Pages",
            "Engagement",
            "Index.cshtml"));

        Assert.DoesNotContain("asp-page=\"/Engagement/Announcements\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page=\"/Engagement/Polls\"", view, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-page=\"/Engagement/Feedback\"", view, StringComparison.Ordinal);

        Assert.Contains("asp-page-handler=\"AnnouncementCreate\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"PollCreate\"", view, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"FeedbackReply\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void HistoricalGetUrlsRemainRedirects()
    {
        var program = File.ReadAllText(Path.Combine(
            FindRoot(),
            "SmartAttendance.Web",
            "Program.cs"));

        Assert.Contains("app.MapGet(\"/Engagement/Announcements\"", program, StringComparison.Ordinal);
        Assert.Contains("/Engagement?tab=announcements", program, StringComparison.Ordinal);

        Assert.Contains("app.MapGet(\"/Engagement/Polls\"", program, StringComparison.Ordinal);
        Assert.Contains("/Engagement?tab=polls", program, StringComparison.Ordinal);

        Assert.Contains("app.MapGet(\"/Engagement/Feedback\"", program, StringComparison.Ordinal);
        Assert.Contains("/Engagement?tab=cases", program, StringComparison.Ordinal);
    }

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