using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.EmployeePortal;

/// <summary>
/// بوابة الموظف (ESS): طلب مناوبة معلَّمة «قابلة للطلب من الخدمة الذاتية»
/// (<c>RequestableFromEss</c>) لمدى أيام — يمرّ بمحرك الموافقات، وعند الاعتماد يُطبَّق
/// تجاوزاً مؤقتاً عبر <see cref="ShiftRequestStore"/>. نفس نمط الطلب المالي الذاتي.
/// </summary>
public class ShiftRequestModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public ShiftRequestModel(ApplicationDbContext db) => _db = db;

    [TempData] public string? StatusMessage { get; set; }

    public List<(int Id, string Name)> Shifts { get; private set; } = new();
    public List<ShiftRequestStore.MyRow> MyRequests { get; private set; } = new();
    public bool HasEmployee { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "ShiftRequest")) return Forbid();
        Shifts = await ShiftRequestStore.RequestableShiftsAsync(_db);
        var employeeId = await ResolveEmployeeIdAsync();
        HasEmployee = employeeId > 0;
        if (employeeId > 0)
            MyRequests = await ShiftRequestStore.ListMineAsync(_db, employeeId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await SelfServiceAccessPolicy.IsAllowedAsync(_db, HttpContext, "ShiftRequest")) return Forbid();
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن إرسال الطلب لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage();
        }

        var form = Request.Form;
        var shiftTypeId = int.TryParse(form["ShiftTypeId"], out var sid) ? sid : 0;
        if (shiftTypeId <= 0 || !DateOnly.TryParse(form["FromDate"], out var from))
        {
            StatusMessage = "المناوبة وتاريخ البداية مطلوبان.";
            return RedirectToPage();
        }
        var to = DateOnly.TryParse(form["ToDate"], out var t) ? t : from;
        var reason = string.IsNullOrWhiteSpace(form["Reason"]) ? null : form["Reason"].ToString().Trim();

        var requestId = await ShiftRequestStore.SubmitAsync(
            _db, employeeId, shiftTypeId, from, to, reason, User.Identity?.Name ?? "Employee");

        StatusMessage = requestId > 0
            ? "تم إرسال طلب المناوبة وهو الآن قيد المراجعة."
            : "تعذّر إرسال الطلب — تأكد أن المناوبة متاحة للطلب.";
        return RedirectToPage();
    }

    private async Task<int> ResolveEmployeeIdAsync()
    {
        var employeeIdClaim = User.FindFirstValue("EmployeeId");
        if (int.TryParse(employeeIdClaim, out var claimEmployeeId) && claimEmployeeId > 0)
            return claimEmployeeId;

        var username = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(username))
            return await HrmsDatabase.ScalarAsync<int>(
                _db,
                "SELECT TOP 1 ISNULL(EmployeeId, 0) FROM AppLoginUsers WHERE Username = @Username AND IsActive = 1",
                command => HrmsDatabase.AddParameter(command, "@Username", username));
        return 0;
    }
}
