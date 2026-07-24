using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeePortal;

/// <summary>
/// صفحة مستقلة: طلب نسيان بصمة (نمط كيان — الخدمة الذاتية). يختار الموظف تاريخاً
/// واحداً ووقت البصمة الغائبة، فيعرض بصمات اليوم مصنَّفةً بالأسبقية الزمنية (دخول/خروج
/// تلقائي) مع ساعات العمل الناتجة. الإرسال يمرّ عبر مراجعة الموارد البشرية.
/// </summary>
public class MissingPunchModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public MissingPunchModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [TempData]
    public string? StatusMessage { get; set; }

    public List<MissingPunchRequestStore.Request> MyRequests { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId > 0)
        {
            MyRequests = await MissingPunchRequestStore.ListAsync(
                _dbContext, new MissingPunchRequestStore.Filter { EmployeeId = employeeId });
        }
        return Page();
    }

    /// <summary>بصمات يوم الموظف (AJAX) مصنَّفةً بالأسبقية — للمعاينة الحيّة.</summary>
    public async Task<IActionResult> OnGetDayPunchesAsync(string? date)
    {
        if (!DateOnly.TryParse(date, out var d))
            return new JsonResult(new { punches = Array.Empty<object>() });

        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            return new JsonResult(new { punches = Array.Empty<object>() });

        var times = await PunchTypingEngine.DayPunchTimesAsync(_dbContext, employeeId, d);
        var typed = PunchTypingEngine.Derive(times);
        return new JsonResult(new
        {
            punches = typed.Select(p => new { at = p.At.ToString("HH:mm"), type = p.Type }).ToArray()
        });
    }

    public async Task<IActionResult> OnPostAsync(string? MpDate, string? MpTime, string? MpReason)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
            employeeId = await HrmsDatabase.ScalarAsync<int>(_dbContext, "SELECT TOP 1 Id FROM Employees ORDER BY Id");
        if (employeeId <= 0)
        {
            StatusMessage = "لا يمكن إرسال الطلب لأن المستخدم غير مرتبط بموظف.";
            return RedirectToPage();
        }

        if (!DateOnly.TryParse(MpDate, out var d) || !TimeOnly.TryParse(MpTime, out var t))
        {
            StatusMessage = "يرجى إدخال تاريخ ووقت البصمة المفقودة.";
            return RedirectToPage();
        }

        var punchAt = d.ToDateTime(t);
        // النوع يُشتَق تلقائياً بالأسبقية الزمنية بين بصمات اليوم والبصمة المُضافة.
        var existingTimes = await PunchTypingEngine.DayPunchTimesAsync(_dbContext, employeeId, d);
        var derivedType = PunchTypingEngine.DeriveTypeFor(existingTimes, punchAt);

        var (ok, message) = await MissingPunchRequestStore.SaveAsync(_dbContext, new MissingPunchRequestStore.Request
        {
            EmployeeId = employeeId,
            PunchAt = punchAt,
            PunchType = derivedType,
            Reason = string.IsNullOrWhiteSpace(MpReason) ? null : MpReason.Trim(),
            Source = "خدمة ذاتية"
        }, User.Identity?.Name ?? "employee");

        StatusMessage = ok
            ? "تم إرسال طلب البصمة المفقودة وهو الآن قيد مراجعة الموارد البشرية."
            : message;
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
                _dbContext,
                "SELECT TOP 1 ISNULL(EmployeeId, 0) FROM AppLoginUsers WHERE Username = @Username AND IsActive = 1",
                command => HrmsDatabase.AddParameter(command, "@Username", username));
        return 0;
    }
}
