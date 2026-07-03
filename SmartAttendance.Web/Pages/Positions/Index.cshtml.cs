using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.Positions;

public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        return RedirectToPage("/OrganizationSettings/Index", new { Tab = "positions" });
    }
}
