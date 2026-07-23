using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.AttendanceRecommendations;

/// <summary>
/// متابعة الإجراءات المقترحة (/AttendanceRecommendations) — المرحلة 4 من مودل
/// الحضور بنمط كيان: فرز مخرجات محرك القواعد (اعتماد ← مخالفة مرتبطة / تجاهل)
/// مع زر «تحليل واسترجاع الاقتراحات». راجع قسمي 10 و13 بدراسة الحضور.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public IndexModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty(SupportsGet = true)]
    public string? Month { get; set; }          // "yyyy-MM"

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "Pending";

    /// <summary>تبويب فرعي داخل «الحركات»: Financial (مالية) أو Violations (مخالفات).</summary>
    [BindProperty(SupportsGet = true)]
    public string SubTab { get; set; } = "Financial";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public const int PageSize = 50;

    public List<RecommendationStore.Recommendation> Rows { get; set; } = new();
    public List<AttendanceTransactionStore.TransactionRow> Transactions { get; set; } = new();
    public List<AttendanceTransactionStore.ViolationRow> ViolationCases { get; set; } = new();
    public int ViolationsCount { get; set; }
    public int TotalRows { get; set; }
    public int TotalPages { get; set; }
    public int PendingCount { get; set; }
    public int ApprovedCount { get; set; }
    public int AutoCount { get; set; }
    public int IgnoredCount { get; set; }
    public int ConflictedCount { get; set; }
    public int TransactionsCount { get; set; }

    public (int Year, int Month) Period
    {
        get
        {
            if (DateTime.TryParse($"{Month}-01", out var parsed)) return (parsed.Year, parsed.Month);
            var today = DateTime.Today;
            return (today.Year, today.Month);
        }
    }

    public async Task OnGetAsync()
    {
        var (year, month) = Period;
        Month ??= $"{year:0000}-{month:00}";

        var all = await RecommendationStore.ListAsync(_dbContext, year, month, null);
        PendingCount = all.Count(r => r.Status == "Pending");
        ApprovedCount = all.Count(r => r.Status == "Approved");
        AutoCount = all.Count(r => r.Status == "Auto");
        IgnoredCount = all.Count(r => r.Status == "Ignored");
        ConflictedCount = all.Count(r => r.Status == "Conflicted");

        Transactions = await AttendanceTransactionStore.ListAsync(_dbContext, year, month);
        TransactionsCount = Transactions.Count;

        // الأثر المالي والأثر التأديبي مفصولان بالتخزين، ويُعرضان بتبويبين
        // فرعيين مطابقةً لكيان (قسم 29.ب): الحركات المالية · حركات المخالفات
        ViolationCases = await AttendanceTransactionStore.ListViolationsAsync(_dbContext, year, month);
        ViolationsCount = ViolationCases.Count;

        // تبويب «الحركات» يعرض الحركات المنفَّذة لا الاقتراحات
        if (Tab == "Transactions")
        {
            TotalRows = SubTab == "Violations" ? ViolationCases.Count : Transactions.Count;
            TotalPages = 1;
            PageNumber = 1;
            return;
        }

        var filtered = Tab == "All" ? all : all.Where(r => r.Status == Tab).ToList();
        TotalRows = filtered.Count;
        TotalPages = TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
        if (PageNumber < 1) PageNumber = 1;
        if (PageNumber > TotalPages) PageNumber = TotalPages;
        Rows = filtered.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
    }

    public async Task<IActionResult> OnPostAnalyzeAsync()
    {
        var (year, month) = Period;
        var (created, auto) = await RecommendationStore.AnalyzeMonthAsync(_dbContext, year, month);
        TempData["SuccessMessage"] = created == 0
            ? "لا اقتراحات جديدة — كل اليوميات المطابقة مقترحة سابقاً أو لا قواعد تنطبق."
            : $"تحليل {month:00}/{year}: {created} اقتراح جديد (منها {auto} تلقائي نُفذ فوراً).";
        return RedirectToPage(new { Month, Tab });
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        var approved = await RecommendationStore.ApproveAsync(_dbContext, id);
        TempData["SuccessMessage"] = approved
            ? "تم الاعتماد — أُنشئت قضية مخالفة مرتبطة (إن كان الأثر مخالفة)."
            : "الاقتراح غير معلق أو غير موجود.";
        return RedirectToPage(new { Month, Tab });
    }

    public async Task<IActionResult> OnPostIgnoreAsync(int id)
    {
        await RecommendationStore.IgnoreAsync(_dbContext, id);
        TempData["SuccessMessage"] = "تم تجاهل الاقتراح.";
        return RedirectToPage(new { Month, Tab });
    }

    /// <summary>ترحيل الحركات المحددة لوحدة الرواتب (نمط كيان «انقل إلى وحدة الرواتب»).</summary>
    public async Task<IActionResult> OnPostTransferAsync(int[] selectedIds)
    {
        var (done, skipped) = await AttendanceTransactionStore.TransferToPayrollAsync(
            _dbContext, selectedIds ?? Array.Empty<int>(), User.Identity?.Name ?? "HR");

        TempData["SuccessMessage"] = done == 0
            ? "لم تُرحَّل أي حركة — المحدد مُرحَّل سابقاً أو نوعه غير قابل للترحيل (مغادرة/إجازة)."
            : $"رُحّلت {done} حركة إلى الرواتب{(skipped > 0 ? $" · تُخطّيت {skipped}" : "")}.";
        return RedirectToPage(new { Month, Tab = "Transactions", SubTab = "Financial" });
    }

    /// <summary>إلغاء ترحيل الحركات المحددة (نظير «إزالة» بكيان).</summary>
    public async Task<IActionResult> OnPostUndoTransferAsync(int[] selectedIds)
    {
        var (done, skipped) = await AttendanceTransactionStore.UndoTransferAsync(
            _dbContext, selectedIds ?? Array.Empty<int>());

        TempData["SuccessMessage"] = done == 0
            ? "لم يُلغَ ترحيل أي حركة — المحدد غير مُرحَّل أو دخل مسيراً مقفلاً."
            : $"أُلغي ترحيل {done} حركة وحُذفت حركة المسير المرتبطة{(skipped > 0 ? $" · تُخطّيت {skipped}" : "")}.";
        return RedirectToPage(new { Month, Tab = "Transactions", SubTab = "Financial" });
    }
}
