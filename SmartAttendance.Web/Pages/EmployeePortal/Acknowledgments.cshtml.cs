using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.EmployeePortal;

/// <summary>
/// بوابة الموظف — الإقرارات المرسَلة إليه: يقرأ ويوافق أو يرفض.
///
/// هذه الصفحة هي **ما يجعل شاشة المتابعة تمتلئ**؛ بدونها تبقى الأختام فارغة أبداً
/// ويصير المودل كلّه إدخالاً يدوياً من HR — أي بلا قيمة إثباتية.
///
/// ⚠️ كل كتابة تمرّ بمعرّف الموظف المستنتَج من هويّته لا بمعرّفٍ يرسله النموذج:
/// موافقةٌ تُكتب باسم موظف آخر إثباتٌ قانوني مزوَّر.
/// </summary>
public class AcknowledgmentsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public AcknowledgmentsModel(ApplicationDbContext db) => _db = db;

    [TempData] public string? StatusMessage { get; set; }

    [BindProperty(SupportsGet = true, Name = "open")] public int? OpenId { get; set; }

    public List<AcknowledgmentStore.Assignment> Items { get; private set; } = new();
    public AcknowledgmentStore.Assignment? Opened { get; private set; }
    public int PendingCount => Items.Count(row => row.NeedsAction);

    public async Task OnGetAsync()
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            return;
        }

        Items = await AcknowledgmentStore.LoadAssignmentsAsync(_db, employeeId);

        if (OpenId is { } id && Items.FirstOrDefault(row => row.Id == id) is { } opened)
        {
            Opened = opened;

            // فتحُ الإقرار **هو** المشاهدة — يُختم هنا لا بزرّ منفصل، وإلا صار
            // «شوهد» إقراراً من الموظف لا واقعةً مرصودة.
            await AcknowledgmentStore.MarkViewedAsync(_db, id, employeeId);
        }
    }

    public async Task<IActionResult> OnPostRespondAsync(int id, bool accept, string? note)
    {
        var employeeId = await ResolveEmployeeIdAsync();
        if (employeeId <= 0)
        {
            StatusMessage = "تعذّر تحديد هويتك.";
            return RedirectToPage();
        }

        await AcknowledgmentStore.RespondAsync(_db, id, employeeId, accept, note);

        StatusMessage = accept
            ? "تم تسجيل موافقتك بتاريخها — شكراً."
            : "تم تسجيل عدم موافقتك مع ملاحظتك.";

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
