using Microsoft.AspNetCore.Mvc;
using SmartAttendance.Application.Announcements.Services;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.Engagement;

/// <summary>
/// شاشة تفاعل الموظف — **شاشة واحدة بتبويبات** بدل خمس صفحات.
///
/// كانت `/Engagement` صفحة «نظرة عامة» لا تفعل شيئاً سوى أنها قائمة روابط
/// تتنكّر بزيّ لوحة: أربع بطاقات KPI ⟵ بطاقة «ملخص التشغيل» تعيد الأرقام
/// نفسها ⟵ أربع بطاقات مودل تكرّر شريط التبويبات فوقها. أربع نقرات للوصول
/// إلى عملٍ، وثلاث نسخ من الرقم ذاته.
///
/// الآن: التبويبات بالشاشة نفسها، وأوّلها **«ما ينتظرك»** — الحالات المفتوحة
/// والمسوّدات غير المنشورة. الأرقام الإجمالية سطرٌ صغير لا أربع بطاقات.
///
/// ⚠️ **منطق الإنشاء والنشر والأرشفة والردّ لم يُنقل ولم يُعَد كتابته**: يبقى
/// بنماذج الصفحات الأربع (`Announcements` · `Polls` · `Feedback`)، وتُرسل إليها
/// النماذج بـ`asp-page`. إعادةُ كتابة منطقٍ ماليّ/تشغيليّ لأجل تغييرٍ بصريّ هي
/// أسرع طريق لانحدارٍ صامت. تلك الصفحات صارت تُعيد التوجيه إلى هنا بعد التنفيذ.
/// </summary>
public class IndexModel : EngagementPageModel
{
    public IndexModel(
        ApplicationDbContext dbContext,
        IAnnouncementService announcementService)
        : base(dbContext, announcementService)
    {
    }

    /// <summary>
    /// التبويب المفتوح — يعبر إعادة التوجيه بعد كل إجراء.
    ///
    /// ⚠️ <c>string?</c> عمداً: خاصيةٌ غير قابلة للعدم ومربوطة بالـGET يعدّها المُقيِّد
    /// «مطلوبة» حين لا يُمرَّر الوسيط بالرابط، فيرتدّ ModelState غير صالح بمجرّد فتح
    /// الصفحة. (رُصد فعلياً بشاشة المخالفات: شريط «راجع الحقول المطلوبة» دائم بلا سبب.)
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; } = "work";

    /// <summary>حالات تنتظر ردّاً أو إغلاقاً.</summary>
    public IReadOnlyList<FeedbackRow> CasesNeedingAction =>
        FeedbackItems
            .Where(x => x.Status.Equals("Open", StringComparison.OrdinalIgnoreCase)
                     || x.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedAt)
            .ToList();

    /// <summary>إعلانات كُتبت ولم تُنشر — عملٌ نصف منجز لا يراه أحد.</summary>
    public IReadOnlyList<AnnouncementRow> DraftAnnouncementRows =>
        Announcements
            .Where(x => !x.IsPublished)
            .OrderByDescending(x => x.PublishDate)
            .ToList();

    /// <summary>استطلاعات لم تُنشر بعد.</summary>
    public IReadOnlyList<PollRow> DraftPollRows =>
        Polls
            .Where(x => !x.IsPublished)
            .OrderByDescending(x => x.PublishDate)
            .ToList();

    /// <summary>
    /// استطلاعات منشورة بلا صوتٍ واحد — منشورة ولا أحد يعلم بها، وهي حالة
    /// تستحقّ الظهور بقائمة العمل لا أن تُحسب «نشطة» وتُنسى.
    /// </summary>
    public IReadOnlyList<PollRow> SilentPolls =>
        Polls
            .Where(x => x.IsPublished && x.VotesCount == 0)
            .ToList();

    public int PendingWorkCount =>
        CasesNeedingAction.Count + DraftAnnouncementRows.Count + DraftPollRows.Count;

    public async Task OnGetAsync()
    {
        await EmployeeEngagementSchema.EnsureAsync(DbContext);
        // خيارات الاستهداف إلزامية هنا: نموذجا الإعلان والاستطلاع بالشاشة نفسها،
        // وبدونها تُعرض منسدلات فارغة فيتعذّر الاستهداف بقسمٍ أو موقعٍ أو أفراد.
        await LoadAudienceOptionsAsync();
        await LoadAnnouncementsAsync();
        await LoadPollsAsync();
        await LoadFeedbackAsync();

        if (!ValidTabs.Contains(Tab))
        {
            Tab = "work";
        }
    }

    private static readonly HashSet<string> ValidTabs = new(StringComparer.OrdinalIgnoreCase)
    {
        "work", "announcements", "polls", "cases", "recognition"
    };
}
