using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.Organization;

/// <summary>
/// Compatibility route for bookmarks that predate the unified organization page.
/// </summary>
public class ChartModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    public IActionResult OnGet()
    {
        var query = CompanyId is > 0 ? $"?ChartCompanyId={CompanyId.Value}" : string.Empty;
        return Redirect($"/Organization/Index{query}#tab=chart");
    }
}
