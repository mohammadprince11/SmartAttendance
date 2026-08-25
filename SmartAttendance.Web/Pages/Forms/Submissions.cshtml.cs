using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Web.Pages.Forms;

/// <summary>
/// متابعة تعبئات النماذج — الطلبات المخصصة والاستبيانات بشاشة واحدة، لأن الباني
/// واحد والمخزن واحد.
/// </summary>
public class SubmissionsModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ICompanyScopeProvider _companyScope;

    public SubmissionsModel(ApplicationDbContext db, ICompanyScopeProvider companyScope)
    {
        _db = db;
        _companyScope = companyScope;
    }

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
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        Templates = await FormTemplateStore.LoadTemplatesAsync(_db);
        Submissions = await FormSubmissionStore.LoadAsync(
            _db, templateId: TemplateFilter,
            status: StatusFilter == "All" ? null : StatusFilter,
            scope: scope);

        if (OpenId is > 0)
        {
            Opened = await FormSubmissionStore.FindAsync(_db, OpenId.Value, scope);
            if (Opened is not null)
            {
                Answers = await FormSubmissionStore.LoadAnswersAsync(_db, Opened.Id);
            }
        }

        // تحليل التقييم يُحسب لقالبٍ محدَّد فقط — متوسّطٌ عبر قوالب مختلفة بلا معنى.
        if (TemplateFilter is > 0)
        {
            RatingAverages = await FormSubmissionStore.RatingAveragesAsync(_db, TemplateFilter.Value, scope);
        }
    }

    public async Task<IActionResult> OnPostReviewAsync(int id, bool approve, string? note)
    {
        if (!approve && string.IsNullOrWhiteSpace(note))
        {
            TempData["ErrorMessage"] = "سبب الرفض إلزامي.";
            return RedirectToPage(new { form = TemplateFilter, statusFilter = StatusFilter });
        }

        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        if (!await FormSubmissionStore.ReviewAsync(_db, id, approve, User.Identity?.Name, note, scope))
        {
            return NotFound();
        }

        TempData["SuccessMessage"] = approve ? "اعتُمد." : "رُفض مع تسجيل السبب.";
        return RedirectToPage(new { form = TemplateFilter, statusFilter = StatusFilter });
    }
}
