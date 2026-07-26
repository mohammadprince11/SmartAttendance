using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.Shifts;

// الطبقة القديمة (Shifts) صارت للعرض فقط — التعديل انتقل إلى «أنواع المناوبات» /ShiftTypes.
public class EditModel : PageModel
{
    public IActionResult OnGet()
    {
        TempData["SuccessMessage"] = "تعديل المناوبات انتقل إلى «أنواع المناوبات». الطبقة القديمة للعرض فقط.";
        return RedirectToPage("./Index");
    }

    public IActionResult OnPost() => RedirectToPage("./Index");
}
