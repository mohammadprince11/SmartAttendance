using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.Shifts;

// الطبقة القديمة (Shifts) صارت للعرض فقط — الإنشاء انتقل إلى «أنواع المناوبات» /ShiftTypes.
// تبقى إعادة التوجيه لحماية الروابط المحفوظة من الكتابة على الجدول القديم.
public class CreateModel : PageModel
{
    public IActionResult OnGet()
    {
        TempData["SuccessMessage"] = "إنشاء المناوبات انتقل إلى «أنواع المناوبات». الطبقة القديمة للعرض فقط.";
        return RedirectToPage("./Index");
    }

    public IActionResult OnPost() => RedirectToPage("./Index");
}
