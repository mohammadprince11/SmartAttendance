using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.EmployeeProfile;

/// <summary>
/// Legacy route. The 360° panels were merged into the unified employee profile
/// (/Employees/Profile → "ملفات الموظف" tab), so this page just redirects there,
/// preserving any old links.
/// </summary>
public class IndexModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int EmployeeId { get; set; }

    public IActionResult OnGet() =>
        RedirectToPage("/Employees/Profile", new { id = EmployeeId });
}
