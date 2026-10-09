using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public class PollLifecycleTests
{
    [Fact]
    public void DatesAreInclusiveAndDraftTakesPrecedence()
    {
        var today = new DateTime(2026, 10, 10);
        Assert.Equal("نشط", PollLifecycle.Status(true, today, today, today));
        Assert.Equal("مجدول", PollLifecycle.Status(true, today.AddDays(1), null, today));
        Assert.Equal("مغلق", PollLifecycle.Status(true, null, today.AddDays(-1), today));
        Assert.Equal("مسودة", PollLifecycle.Status(false, null, today.AddDays(-1), today));
        Assert.False(PollLifecycle.ValidDates(today, today.AddDays(-1)));
        Assert.True(PollLifecycle.ValidDates(today, today));
        Assert.True(PollLifecycle.ValidDates(null, null));
    }
    [Fact]
    public void ConfidentialResultsRequireFiveResponses()
    {
        Assert.False(PollLifecycle.CanShowResults(true, 4));
        Assert.True(PollLifecycle.CanShowResults(true, 5));
        Assert.True(PollLifecycle.CanShowResults(false, 0));
    }
    [Fact]
    public void BothVotingSurfacesAndTheirListsEnforceDates()
    {
        var root = FindRoot();
        foreach (var relative in new[] { "Pages/EmployeePortal/Index.cshtml.cs", "Controllers/Api/MeController.cs" })
        {
            var source = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", relative));
            Assert.True(source.Split("p.StartsOn <= CAST(DATEADD(hour,3,SYSUTCDATETIME()) AS date)").Length >= 3);
            Assert.True(source.Split("p.EndsOn >= CAST(DATEADD(hour,3,SYSUTCDATETIME()) AS date)").Length >= 3);
        }
    }
    [Fact]
    public void PollDropdownPortalRemainsInsideModalTopLayer()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "SmartAttendance.Web", "wwwroot", "js", "zynora-select-system.js"));
        Assert.Contains("select.closest(\"dialog.zy-poll-dialog[open]\")", source);
        Assert.Contains("(pollDialog || document.body).appendChild(panel)", source);
        Assert.Contains("pollDialog.addEventListener(\"close\", closeOpen", source);
    }
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "SmartAttendance.Web", "Pages"))) return directory.FullName;
        throw new InvalidOperationException("Source root missing.");
    }
    [Fact]
    public void PollEmployeePickerStaysInDialogAndRestoresHostOnClose()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "SmartAttendance.Web", "wwwroot", "js", "zynora-employee-picker.js"));
        Assert.Contains("root.closest('dialog.zy-poll-dialog[open]')", source);
        Assert.Contains("(pollDialog || document.body).appendChild(m.back)", source);
        Assert.Contains("e.preventDefault(); e.stopPropagation();", source);
        Assert.Contains("document.body.appendChild(back)", source);
    }
    [Fact]
    public void PollBranchChoicesExcludeDeletedSitesAndKeepRealIds()
    {
        var view = File.ReadAllText(Path.Combine(FindRoot(), "SmartAttendance.Web", "Pages", "Engagement", "Index.cshtml"));
        Assert.Contains("<option value=\"@b.Id\">@b.Name</option>", view);
        var model = File.ReadAllText(Path.Combine(FindRoot(), "SmartAttendance.Web", "Pages", "Engagement", "EngagementPageModel.cs"));
        Assert.Contains("SELECT Id, Name FROM Branches WHERE ISNULL(IsDeleted,0)=0 AND {branchScope}", model);
        Assert.Contains("WHERE Id=@Id AND ISNULL(IsDeleted,0)=0 AND", model);
        var guard = model[model.IndexOf("protected async Task<bool> IsTargetWithinCompanyScopeAsync")..];
        Assert.True(guard.IndexOf("ISNULL(IsDeleted,0)=0") < guard.IndexOf("if (scope.IsUnrestricted) return true;"));
    }
}
