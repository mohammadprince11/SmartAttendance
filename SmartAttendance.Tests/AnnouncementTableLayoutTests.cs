namespace SmartAttendance.Tests;

public class AnnouncementTableLayoutTests
{
    [Fact]
    public void AnnouncementColumnsHaveExplicitProportionsWithoutAffectingPollTable()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "SmartAttendance.Web", "Pages"))) root = root.Parent;
        Assert.NotNull(root);
        var view = File.ReadAllText(Path.Combine(root!.FullName, "SmartAttendance.Web/Pages/Engagement/Index.cshtml"));
        var css = File.ReadAllText(Path.Combine(root.FullName, "SmartAttendance.Web/wwwroot/css/zynora-announcement-management.css"));
        Assert.Contains("zy-ann-list-table", view);
        Assert.Contains("<colgroup><col class=\"zy-ann-col-sequence\"", view);
        Assert.Contains("col.zy-ann-col-sequence{width:25%}", css);
        Assert.Contains("col.zy-ann-col-category{width:25%}", css);
        Assert.Contains("col.zy-ann-col-title{width:25%}", css);
        Assert.Contains("col.zy-ann-col-date{width:25%}", css);
        Assert.Contains(".zyw .zy-ann-list-table th,.zyw .zy-ann-list-table td{text-align:center!important;", css);
        Assert.Contains(".zyw .zy-ann-list-table .zy-ann-title{padding:0;line-height:1.6;text-align:center!important}", css);
        Assert.Contains(".zyw .zy-ann-list-table th:nth-child(n){width:auto;text-align:center!important}", css);
        Assert.Contains("data-ann-open=\"zy-ann-@item.Id\"", view);
        Assert.Contains("zy-ann-table zy-poll-table", view);
    }
}
