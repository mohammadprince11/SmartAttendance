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

    private readonly SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider _companyScope;

    public IndexModel(
        ApplicationDbContext dbContext,
        SmartAttendance.Web.Infrastructure.Security.ICompanyScopeProvider companyScope)
    {
        _dbContext = dbContext;
        _companyScope = companyScope;
    }

    [BindProperty(SupportsGet = true)]
    public string? Month { get; set; }          // "yyyy-MM"

    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "Pending";

    /// <summary>تبويب فرعي داخل «الحركات»: Financial (مالية) أو Violations (مخالفات).</summary>
    [BindProperty(SupportsGet = true)]
    public string SubTab { get; set; } = "Financial";

    /// <summary>بحث برمز الموظف أو اسمه — خمسة آلاف اقتراح لا تُفرَز بالتصفّح.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>قصر العرض على قاعدة بعينها (0 = الكل) — لأن قرار «كل الغياب» يختلف عن قرار «كل تأخير».</summary>
    [BindProperty(SupportsGet = true)]
    public int RuleId { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    /// <summary>القواعد الحاضرة بنتائج الشهر — منتقي الفلترة يُبنى منها لا من كل القواعد.</summary>
    public List<(int Id, string Name, int Count)> RuleFilterOptions { get; set; } = new();

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

    /// <summary>قواعد تسمح بتعديل إجرائها الموصى به — تُظهر أدوات التعديل بالصفّ.</summary>
    public HashSet<int> EditableRuleIds { get; set; } = new();

    /// <summary>
    /// قواعد أثرها **نسبة من قيمة اليوم** لا مبلغاً. لولاها لعُرضت «25 مبلغ» —
    /// رقمٌ يقرؤه المستخدم ديناراً وهو نسبة مئوية.
    /// </summary>
    public HashSet<int> PercentRuleIds { get; set; } = new();

    /// <summary>الفترة المعروضة بعد تطبيق سياسة غلق الحضور (قد تعبر حدّ الشهر).</summary>
    public AttendancePeriodPolicy.Period CutoffPeriod { get; private set; }

    /// <summary>اسم السياسة المطبَّقة — يُعرض ليعرف المستخدم لمَ تغيّرت حدود الفترة.</summary>
    public string? CutoffPolicyName { get; private set; }

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

        // الفترة تتبع **سياسة غلق الحضور** لا الشهر التقويمي (نفس ما يفعله الحضور
        // اليومي والمسير). بلا سياسة تبقى الفترة شهراً كاملاً، فلا تنكسر شركةٌ لم
        // تُعرّف سياسةً — والعرض والتحليل يتّفقان على الحدود نفسها.
        (CutoffPeriod, CutoffPolicyName) =
            await AttendancePeriodPolicy.ResolveFromPolicyAsync(_dbContext, year, month);

        var all = await RecommendationStore.ListRangeAsync(
            _dbContext, CutoffPeriod.From, CutoffPeriod.To, null);
        PendingCount = all.Count(r => r.Status == "Pending");
        ApprovedCount = all.Count(r => r.Status == "Approved");
        AutoCount = all.Count(r => r.Status == "Auto");
        IgnoredCount = all.Count(r => r.Status == "Ignored");
        ConflictedCount = all.Count(r => r.Status == "Conflicted");

        // الحركات والقضايا تتبع الفترة نفسها: تُقرأ لكل شهرٍ تلمسه ثم تُقصّ عليها،
        // فلا يختلف تبويبٌ عن آخر بالحدود ولا تظهر حركةٌ خارج ما يُعرض.
        var covered = CutoffPeriod.CoveredMonths().ToList();

        Transactions = new List<AttendanceTransactionStore.TransactionRow>();
        foreach (var (coveredYear, coveredMonth) in covered)
        {
            Transactions.AddRange(
                await AttendanceTransactionStore.ListAsync(_dbContext, coveredYear, coveredMonth));
        }
        Transactions = Transactions
            .Where(row => row.WorkDate >= CutoffPeriod.From && row.WorkDate <= CutoffPeriod.To)
            .ToList();

        TransactionsCount = Transactions.Count;

        // الأثر المالي والأثر التأديبي مفصولان بالتخزين، ويُعرضان بتبويبين
        // فرعيين مطابقةً لكيان (قسم 29.ب): الحركات المالية · حركات المخالفات
        ViolationCases = new List<AttendanceTransactionStore.ViolationRow>();
        foreach (var (coveredYear, coveredMonth) in covered)
        {
            ViolationCases.AddRange(
                await AttendanceTransactionStore.ListViolationsAsync(_dbContext, coveredYear, coveredMonth));
        }
        ViolationCases = ViolationCases
            .Where(row => row.EventDate >= CutoffPeriod.From && row.EventDate <= CutoffPeriod.To)
            .ToList();

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

        // منتقي القواعد يُبنى **بعد فلتر الحالة وقبل فلتر القاعدة**: فيعرض ما هو
        // قابل للاختيار فعلاً بهذا التبويب، ولا يختفي الخيار المختار من قائمته.
        RuleFilterOptions = filtered
            .GroupBy(row => (row.RuleId, row.RuleName))
            .Select(group => (group.Key.RuleId, group.Key.RuleName, Count: group.Count()))
            .OrderByDescending(option => option.Count)
            .ToList();

        if (RuleId != 0)
        {
            filtered = filtered.Where(row => row.RuleId == RuleId).ToList();
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            filtered = filtered
                .Where(row => row.EmployeeNo.Contains(term, StringComparison.OrdinalIgnoreCase)
                           || row.EmployeeName.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        TotalRows = filtered.Count;
        TotalPages = TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
        if (PageNumber < 1) PageNumber = 1;
        if (PageNumber > TotalPages) PageNumber = TotalPages;
        Rows = filtered.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
        EditableRuleIds = await RecommendationStore.EditableRuleIdsAsync(_dbContext);

        PercentRuleIds = (await ShiftRuleStore.ListAsync(_dbContext))
            .Where(rule => ShiftRuleStore.IsPercentOfDay(rule.ValueKind, rule.ActionType))
            .Select(rule => rule.Id)
            .ToHashSet();
    }

    /// <summary>تعديل الإجراء الموصى به — المتجر يفرض راية «السماح بالتعديل» على القاعدة.</summary>
    public async Task<IActionResult> OnPostEditActionAsync(int id, string actionText, decimal actionValue)
    {
        var (ok, message) = await RecommendationStore.UpdateActionAsync(
            _dbContext, id, actionText ?? string.Empty, actionValue);
        TempData[ok ? "SuccessMessage" : "ErrorMessage"] = message;
        return RedirectToPage(new { Month, Tab, SubTab, PageNumber });
    }

    public async Task<IActionResult> OnPostAnalyzeAsync()
    {
        var (year, month) = Period;

        // فترة الغلق (21 → 20) تعبر حدّ الشهر، والمحرّك يُقيَّم شهراً تقويمياً —
        // فيُشغَّل على **كل شهرٍ تلمسه الفترة**، وإلا بقي نصفها بلا اقتراحات بينما
        // تعرضه الشاشة فارغاً كأن لا مخالفة فيه. نفس ما يفعله الحضور اليومي.
        var (period, _) = await AttendancePeriodPolicy.ResolveFromPolicyAsync(_dbContext, year, month);

        var created = 0;
        var auto = 0;

        foreach (var (coveredYear, coveredMonth) in period.CoveredMonths())
        {
            var (monthCreated, monthAuto) =
                await RecommendationStore.AnalyzeMonthAsync(_dbContext, await _companyScope.GetAsync(), coveredYear, coveredMonth);
            created += monthCreated;
            auto += monthAuto;
        }

        var label = $"{period.From:yyyy-MM-dd} → {period.To:yyyy-MM-dd}";

        TempData["SuccessMessage"] = created == 0
            ? $"لا اقتراحات جديدة للفترة {label} — كل اليوميات المطابقة مقترحة سابقاً أو لا قواعد تنطبق."
            : $"تحليل الفترة {label}: {created} اقتراح جديد (منها {auto} تلقائي نُفذ فوراً).";

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

    /// <summary>
    /// اعتماد جماعي للمحدَّد. **كل اعتماد يُنشئ أثراً حقيقياً** (قضية مخالفة أو حركة
    /// مالية)، فالنطاق مقصورٌ على ما أشّر عليه المستخدم بالصفحة — لا على الفلتر كلّه:
    /// خمسة آلاف اقتراح باعتمادٍ واحد قرارٌ لا يُتخذ بزرّ.
    /// </summary>
    public async Task<IActionResult> OnPostBulkApproveAsync(int[] selectedIds)
    {
        var ids = (selectedIds ?? Array.Empty<int>()).Distinct().ToList();
        var done = 0;

        foreach (var id in ids)
        {
            if (await RecommendationStore.ApproveAsync(_dbContext, id)) done++;
        }

        TempData[done == 0 ? "ErrorMessage" : "SuccessMessage"] = done == 0
            ? "لم يُعتمد شيء — المحدَّد مبتوتٌ سابقاً أو غير موجود."
            : $"اعتُمد {done} اقتراحاً — نُفِّذ أثر كلٍّ منها (قضية مخالفة أو حركة)"
              + (ids.Count > done ? $" · تُخطّي {ids.Count - done} (مبتوت سابقاً)." : ".");

        return RedirectToPage(new { Month, Tab, Search, RuleId, PageNumber });
    }

    /// <summary>تجاهل جماعي: يُغلق المحدَّد بلا أي أثر، ولا يُعاد توليده بتحليلٍ لاحق.</summary>
    public async Task<IActionResult> OnPostBulkIgnoreAsync(int[] selectedIds)
    {
        var ids = (selectedIds ?? Array.Empty<int>()).Distinct().ToList();

        foreach (var id in ids)
        {
            await RecommendationStore.IgnoreAsync(_dbContext, id);
        }

        TempData["SuccessMessage"] = ids.Count == 0
            ? "لم يُحدَّد شيء."
            : $"تُجوهل {ids.Count} اقتراحاً — بلا أثر، ولن يُعاد توليدها.";

        return RedirectToPage(new { Month, Tab, Search, RuleId, PageNumber });
    }

    /// <summary>ترحيل الحركات المحددة لوحدة الرواتب (نمط كيان «انقل إلى وحدة الرواتب»).</summary>
    public async Task<IActionResult> OnPostTransferAsync(int[] selectedIds)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var (done, skipped) = await AttendanceTransactionStore.TransferToPayrollAsync(
            _dbContext, scope, selectedIds ?? Array.Empty<int>(), User.Identity?.Name ?? "HR");

        TempData["SuccessMessage"] = done == 0
            ? "لم تُرحَّل أي حركة — المحدد مُرحَّل سابقاً أو نوعه غير قابل للترحيل (مغادرة/إجازة)."
            : $"رُحّلت {done} حركة إلى الرواتب{(skipped > 0 ? $" · تُخطّيت {skipped}" : "")}.";
        return RedirectToPage(new { Month, Tab = "Transactions", SubTab = "Financial" });
    }

    /// <summary>إلغاء ترحيل الحركات المحددة (نظير «إزالة» بكيان).</summary>
    public async Task<IActionResult> OnPostUndoTransferAsync(int[] selectedIds)
    {
        var scope = await _companyScope.GetAsync(HttpContext.RequestAborted);
        var (done, skipped) = await AttendanceTransactionStore.UndoTransferAsync(
            _dbContext, scope, selectedIds ?? Array.Empty<int>());

        TempData["SuccessMessage"] = done == 0
            ? "لم يُلغَ ترحيل أي حركة — المحدد غير مُرحَّل أو دخل مسيراً مقفلاً."
            : $"أُلغي ترحيل {done} حركة وحُذفت حركة المسير المرتبطة{(skipped > 0 ? $" · تُخطّيت {skipped}" : "")}.";
        return RedirectToPage(new { Month, Tab = "Transactions", SubTab = "Financial" });
    }
}
