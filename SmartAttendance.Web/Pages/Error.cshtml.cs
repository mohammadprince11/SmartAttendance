using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;

namespace SmartAttendance.Web.Pages
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [IgnoreAntiforgeryToken]
    // صفحة الخطأ عامّة عمداً: لو طلبت مصادقةً لصار أي خطأ بمسار المصادقة نفسه
    // حلقةَ إعادة توجيه لا تنتهي بدل رسالةٍ مفهومة.
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public class ErrorModel : PageModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        public void OnGet()
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        }
    }

}
