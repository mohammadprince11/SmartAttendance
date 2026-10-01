using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.OrgStructures;

/// <summary>
/// Compatibility route for the retired duplicate organization-structures screen.
/// </summary>
public class IndexModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? CompanyId { get; set; }

    public IActionResult OnGet()
    {
        var query = CompanyId is > 0 ? $"?ChartCompanyId={CompanyId.Value}" : string.Empty;
        return Redirect($"/Organization/Index{query}#tab=company");
    }
}
