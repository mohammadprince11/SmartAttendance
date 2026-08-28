using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartAttendance.Web.Pages.Settings;

/// <summary>
/// بوابة إدارية لا تخزّن إعداداً جديداً؛ تجمع مصادر الحقيقة الفعلية فقط حتى لا
/// تتكرر السياسات في شاشة شكلية منفصلة.
/// </summary>
[Authorize(Roles = "Admin")]
public sealed class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}
