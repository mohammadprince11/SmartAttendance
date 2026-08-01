using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Forms;

/// <summary>
/// متابعة تعبئات النماذج — الطلبات المخصصة والاستبيانات بشاشة واحدة، لأن الباني
/// واحد والمخزن واحد.
/// </summary>
public class SubmissionsModel : PageModel
{
    private readonly ApplicationDbContext _db;

    public SubmissionsModel(ApplicationDbContext db) => _db = db;

    [BindProperty(SupportsGet = true, Name = "form")] public int? TemplateFilter { get; set; }
    [BindProperty(SupportsGet = true, Name = "open")] public int? OpenId { get; set; }
    [BindProperty(SupportsGet = true)] public string StatusFilter { get; set; } = "All";

    public List<FormTemplateStore.Template> Templates { get; private set; } = new();
    public List<FormSubmissionStore.Submission> Submissions { get; private set; } = new();
    public FormSubmissionStore.Submission? Opened { get; private set; }
    public List<FormSubmissionStore.Answer> Answers { get; private set; } = new();
    public Dictionary<string, (decimal Average, int Count)> RatingAverages { get; private set; } = new();

    public int PendingCount => Submissions.Count(row => row.IsPending);

    public async Task OnGetAsync()
    {
        Templates = await FormTemplateStore.LoadTemplatesAsync(_db);
        Submissions = await FormSubmissionStore.LoadAsync(
            _db, templateId: TemplateFilter, status: StatusFilter == "All" ? null : StatusFilter);

        if (OpenId is > 0)
        {
            Opened = await FormSubmissionStore.FindAsync(_db, OpenId.Value);
            if (Opened is not null)
            {
                Answers = await FormSubmissionStore.LoadAnswersAsync(_db, Opened.Id);
            }
        }

        // تحليل التقييم يُحسب لقالبٍ محدَّد فقط — متوسّطٌ عبر قوالب مختلفة بلا معنى.
        if (TemplateFilter is > 0)
        {
            RatingAverages = await FormSubmissionStore.RatingAveragesAsync(_db, TemplateFilter.Value);
        }
    }

    public async Task<IActionResult> OnPostReviewAsync(int id, bool approve, string? note)
    {
        if (!approve && string.IsNullOrWhiteSpace(note))
        {
            TempData["ErrorMessage"] = "سبب الرفض إلزامي.";
            return RedirectToPage(new { form = TemplateFilter, statusFilter = StatusFilter });
        }

        await FormSubmissionStore.ReviewAsync(_db, id, approve, User.Identity?.Name, note);

        TempData["SuccessMessage"] = approve ? "اعتُمد." : "رُفض مع تسجيل السبب.";
        return RedirectToPage(new { form = TemplateFilter, statusFilter = StatusFilter });
    }
}
