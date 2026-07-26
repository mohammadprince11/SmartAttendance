using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.Shifts;

// الطبقة القديمة (Shifts) صارت للعرض فقط — لا حذف من الواجهة (يقرأها ملف الموظف والإعداد).
public class DeleteModel : PageModel
{
    public IActionResult OnGet()
    {
        TempData["SuccessMessage"] = "الحذف معطّل في الطبقة القديمة. أدِر المناوبات من «أنواع المناوبات».";
        return RedirectToPage("./Index");
    }

    public IActionResult OnPost() => RedirectToPage("./Index");
}
