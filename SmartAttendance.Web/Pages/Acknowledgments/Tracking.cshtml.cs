using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Acknowledgments;

/// <summary>
/// متابعة الإقرارات (نظير «متابعة الإقرارات» بكيان) — **هنا القيمة الحقيقية**.
///
/// الأعمدة تفصل **المشاهدة** عن **الموافقة** عمداً: «أُرسل ولم يُفتح» هو بالضبط ما
/// تحتاجه الشركة لتقول «أُبلِغ ولم يستجب»، وهو موقف قانوني مختلف تماماً عن
/// «فُتح ولم يُوافَق عليه».
/// </summary>
public class TrackingModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public TrackingModel(ApplicationDbContext db)
    {
        _db = db;
    }

    // ⚠️ بلا Name مخصَّص عمداً: asp-for يولّد name="TemplateFilter"/"PendingOnly"،
    // فاسمٌ مخالف بالربط يعني مرشّحاً يُرسَل ولا يُقرأ.
    [BindProperty(SupportsGet = true)] public int? TemplateFilter { get; set; }
    [BindProperty(SupportsGet = true)] public bool PendingOnly { get; set; }

    public List<AcknowledgmentStore.Assignment> Assignments { get; private set; } = new();
    public List<AcknowledgmentStore.Template> Templates { get; private set; } = new();

    public int TotalSent => Assignments.Count;
    public int TotalViewed => Assignments.Count(row => row.Viewed);
    public int TotalAccepted => Assignments.Count(row => row.Accepted);
    public int TotalDeclined => Assignments.Count(row => row.Declined);
    /// <summary>أُرسل ولم يُفتح — الرقم الذي يفتح الشاشة عادةً.</summary>
    public int NeverOpened => Assignments.Count(row => !row.Viewed);

    public async Task OnGetAsync()
    {
        Templates = await AcknowledgmentStore.LoadTemplatesAsync(_db);
        Assignments = await AcknowledgmentStore.LoadAssignmentsAsync(
            _db, templateId: TemplateFilter, pendingOnly: PendingOnly);
    }

    public async Task<IActionResult> OnPostRemindAsync(int id)
    {
        // إعادة إرسال = صفّ جديد بختم زمني جديد، لا تعديل للصفّ القائم — وإلا ضاع
        // إثبات الإبلاغ الأول.
        var assignment = (await AcknowledgmentStore.LoadAssignmentsAsync(_db))
            .FirstOrDefault(row => row.Id == id);

        if (assignment is not null)
        {
            await AcknowledgmentStore.SendAsync(
                _db, assignment.TemplateId, new[] { assignment.EmployeeId }, User.Identity?.Name);
            TempData["SuccessMessage"] = "أُعيد الإرسال — سُجِّل كإبلاغ مستقل بختمه الزمني.";
        }

        return RedirectToPage(new { templateFilter = TemplateFilter, pendingOnly = PendingOnly });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await AcknowledgmentStore.DeleteAssignmentAsync(_db, id);
        TempData["SuccessMessage"] = "حُذف الإرسال (المحسوم لا يُحذف).";
        return RedirectToPage(new { templateFilter = TemplateFilter, pendingOnly = PendingOnly });
    }
}
