using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.Shifts;

// الطبقة القديمة (Shifts) صارت للعرض فقط — الاستيراد معطّل. عرّف المناوبات من «أنواع المناوبات» /ShiftTypes.
public class ImportModel : PageModel
{
    public IActionResult OnGet()
    {
        TempData["SuccessMessage"] = "استيراد الشفتات القديمة معطّل. عرّف المناوبات من «أنواع المناوبات».";
        return RedirectToPage("./Index");
    }

    public IActionResult OnPostPreview() => RedirectToPage("./Index");

    public IActionResult OnPostImport() => RedirectToPage("./Index");
}
