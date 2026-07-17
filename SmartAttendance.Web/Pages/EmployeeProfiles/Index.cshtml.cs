using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.EmployeeProfiles;

/// <summary>
/// Legacy route. The HR operations mega-page was split into the Engagement hub
/// (/Engagement + Announcements/Polls/Feedback/Recognition). Old links and
/// bookmarks land here and are forwarded to the matching new page.
/// </summary>
public class IndexModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; }

    public IActionResult OnGet()
    {
        var tab = (Tab ?? "overview").Trim().ToLowerInvariant();

        var target = tab switch
        {
            "announcements" or "wall" => "/Engagement/Announcements",
            "polls" => "/Engagement/Polls",
            "feedback" => "/Engagement/Feedback",
            "recognition" or "campaigns" => "/Engagement/Recognition",
            _ => "/Engagement/Index"
        };

        return RedirectToPagePermanent(target);
    }
}
