using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Pages.DayAttendance;

/// <summary>
/// الحضور اليومي (/DayAttendance) — المرحلة 3 من مودل الحضور بنمط كيان:
/// يوميات موظف×يوم بالحقول المشتقة (تأخير/خروج مبكر/ساعات/حالة/تم التحليل)،
/// وزر «تحديث الحضور» يعيد بناء الشهر من البصمات الخام مقابل مناوبة مختارة.
/// راجع قسمي 9 و15 بدراسة الحضور.
/// </summary>
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly SmartAttendance.Web.Infrastructure.Notifications.IWebPushSender _push;

    public IndexModel(
        ApplicationDbContext dbContext,
        SmartAttendance.Web.Infrastructure.Notifications.IWebPushSender push)
    {
        _dbContext = dbContext;
        _push = push;
    }

    [BindProperty(SupportsGet = true)]
    public string? Month { get; set; }          // "yyyy-MM"

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>
    /// فلتر الحالة من أزرار العدّادات أعلى الشاشة: Present · Late · Incomplete ·
    /// Absent. فارغ = الكل. العدّادات تبقى محسوبة على **كل** الشهر لا على
    /// المفلتَر، وإلا صار الضغط على «متأخر» يصفّر بقية الأزرار فيستحيل الرجوع.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public const int PageSize = 50;

    public List<DayAttendanceStore.DayRow> Rows { get; set; } = new();
    public List<ShiftTypeStore.ShiftType> Shifts { get; set; } = new();

    /// <summary>
    /// أنواع الطلبات القابلة للتقديم نيابةً — كتالوج <see cref="RequestTypeStore"/>
    /// مقصوراً على ما له أثرٌ منفَّذ (<see cref="BulkRequestStore.ResolveEffect"/>).
    /// عرض نوعٍ بلا أثر كان يعني زرّاً يكتب صفّاً لا يقرؤه محرّك.
    /// </summary>
    public List<RequestTypeStore.ReqType> RequestTypes { get; set; } = new();

    /// <summary>الأنواع الزمنية (مغادرات) — تُظهر حقلي الوقت بالمنبثقة.</summary>
    public HashSet<int> TimedTypeIds { get; set; } = new();

    /// <summary>تقرير المستثنَين من آخر تقديمٍ جماعي (القرار 1: استثنِ مع تقرير).</summary>
    public List<BulkRequestStore.Skipped> LastSkips { get; set; } = new();

    /// <summary>كل المناوبات بمعرّفها — لقراءة نافذة دوام الصفّ (تشمل المعطّلة: صفٌّ قديم قد يحملها).</summary>
    private Dictionary<int, ShiftTypeStore.ShiftType> _allShifts = new();

    /// <summary>
    /// نافذة دوام الصفّ ("HH:mm") من مناوبته ويومِ أسبوعه — مصدر الاقتراح التلقائي
    /// لوقت المغادرة: التأخير = [بداية الدوام ← الدخول الفعلي]، والخروج المبكر =
    /// [الخروج الفعلي ← نهاية الدوام]. المناوبة بفتراتٍ متعددة تُقرأ بأول فترة وآخرها.
    /// </summary>
    public (string? Start, string? End) ShiftWindow(DayAttendanceStore.DayRow row)
    {
        if (row.ShiftTypeId is not int shiftId || !_allShifts.TryGetValue(shiftId, out var shift))
            return (null, null);

        var day = shift.Days.FirstOrDefault(d => d.DayIndex == DayAttendanceStore.ToDayIndex(row.WorkDate));
        var start = day?.StartTime;
        var end = day?.EndTime;

        if (string.IsNullOrWhiteSpace(start) && shift.Periods.Count > 0)
        {
            start = shift.Periods[0].StartTime;
            end = shift.Periods[^1].EndTime;
        }

        return (Hhmm(start), Hhmm(end));
    }

    /// <summary>"HH:mm" أو فارغ — القيمة تدخل حقل <c>input[type=time]</c> فلا تحتمل صيغةً أخرى.</summary>
    private static string? Hhmm(string? value) =>
        TimeSpan.TryParse(value, out var parsed) ? parsed.ToString(@"hh\:mm") : null;
    public int TotalRows { get; set; }
    public int TotalPages { get; set; }
    public int PresentCount { get; set; }
    public int LateCount { get; set; }
    public int AbsentCount { get; set; }
    public int IncompleteCount { get; set; }

    /// <summary>يوميات طرأ عليها تغيير بعد التحليل — «لم تُحلَّل» بواجهة الفلترة.</summary>
    public int StaleCount { get; set; }

    /// <summary>
    /// عدد الموظفين المميَّزين بالعرض الحالي — نطاق زرّ «إشعار الموظفين»، ويُعرض
    /// على الزرّ نفسه كي يعرف المستخدم كم سيُشعِر **قبل** أن يضغط.
    /// </summary>
    public int NotifyEmployeeCount { get; set; }

    /// <summary>
    /// مفتاح فلتر البائتة — مُعرَّف بـ<see cref="DayAttendanceStore.StaleFilterKey"/>
    /// حيث يعيش منطق الفلترة. يبقى هنا اسماً مألوفاً للواجهة (`IndexModel.StaleFilterKey`).
    /// </summary>
    public const string StaleFilterKey = DayAttendanceStore.StaleFilterKey;

    /// <summary>الفترة الفعلية المعروضة بعد تطبيق سياسة الغلق.</summary>
    public AttendancePeriodPolicy.Period CutoffPeriod { get; private set; }

    /// <summary>اسم سياسة الغلق المطبَّقة — يُعرض ليعرف المستخدم لمَ تغيّرت الحدود.</summary>
    public string? CutoffPolicyName { get; private set; }

    /// <summary>
    /// يقرأ سياسة غلق **الحضور** النشطة ويحسب منها الفترة. بلا سياسة يبقى
    /// السلوك القديم (الشهر التقويمي كاملاً) فلا تنكسر شركةٌ لم تُعرّف سياسةً.
    /// </summary>
    private async Task<AttendancePeriodPolicy.Period> ResolvePeriodAsync(int year, int month)
    {
        // المنطق مركزيّ بـAttendancePeriodPolicy — كان مكرّراً هنا وحده، فكانت بقية
        // الشاشات تعرض الشهر التقويمي بينما المسير يقرأ فترة الغلق.
        var (period, policyName) =
            await AttendancePeriodPolicy.ResolveFromPolicyAsync(_dbContext, year, month);

        CutoffPolicyName = policyName;
        return period;
    }



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

        var shifts = await ShiftTypeStore.ListAsync(_dbContext);
        _allShifts = shifts.ToDictionary(shift => shift.Id);
        Shifts = shifts.Where(s => s.IsActive).ToList();

        await LoadRequestCatalogAsync();

        // تقرير الاستثناء يعبر PRG كـJSON: TempData لا يحمل كائنات، ونصٌّ واحد لا
        // يكفي — المستخدم يحتاج «من» و«لماذا» صفّاً صفّاً.
        if (TempData["BulkSkips"] is string skipsJson && skipsJson.Length > 0)
        {
            LastSkips = System.Text.Json.JsonSerializer
                .Deserialize<List<BulkRequestStore.Skipped>>(skipsJson) ?? new();
        }

        // الفترة تتبع سياسة غلق الحضور (مثلاً 21 → 20) لا الشهر التقويمي.
        CutoffPeriod = await ResolvePeriodAsync(year, month);

        var all = await DayAttendanceStore.ListRangeAsync(
            _dbContext, CutoffPeriod.From, CutoffPeriod.To, Search);
        PresentCount = all.Count(r => r.Status == "Present");
        LateCount = all.Count(r => r.Status == "Late");
        AbsentCount = all.Count(r => r.Status == "Absent");
        IncompleteCount = all.Count(r => r.Status == "Incomplete");
        StaleCount = all.Count(r => r.IsStale);

        // الفلتر يُطبَّق بعد العدّادات، والترتيب من الأحدث للأقدم ثم برقم الموظف
        // ليكون ترتيب الصفحات ثابتاً (بلا مُرتِّب ثانوي تتبدّل الصفوف بين الصفحات).
        var view = ApplyStatusFilter(all)
            .OrderByDescending(row => row.WorkDate)
            .ThenBy(row => row.EmployeeNo, StringComparer.Ordinal)
            .ToList();

        // موظّفو العرض الحالي — نطاق زرّ الإشعار. **مميَّزون** لا صفوف: الموظف
        // المتأخر خمسة أيام إنسانٌ واحد يُشعَر مرّة، لا خمس رسائل.
        NotifyEmployeeCount = view.Select(row => row.EmployeeId).Distinct().Count();

        TotalRows = view.Count;
        TotalPages = TotalRows == 0 ? 1 : (int)Math.Ceiling(TotalRows / (double)PageSize);
        if (PageNumber < 1) PageNumber = 1;
        if (PageNumber > TotalPages) PageNumber = TotalPages;
        Rows = view.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
    }

    /// <summary>
    /// فلتر الحالة — مشترك بين العرض وزرّ الإشعار، ومنطقه بـ
    /// <see cref="DayAttendanceStore.FilterByStatus"/> (دالّة نقيّة مُختبَرة).
    /// </summary>
    private List<DayAttendanceStore.DayRow> ApplyStatusFilter(
        IEnumerable<DayAttendanceStore.DayRow> rows) =>
        DayAttendanceStore.FilterByStatus(rows, StatusFilter);

    /// <summary>
    /// كتالوج الطلبات القابل للتقديم من هذه الشاشة. لا يُسقط الصفحة عند تعذّره —
    /// شاشة الحضور تعمل بلا كتالوج، والزرّ وحده يختفي.
    /// </summary>
    private async Task LoadRequestCatalogAsync()
    {
        try
        {
            await RequestTypeStore.EnsureAsync(_dbContext);
            var types = await RequestTypeStore.ListTypesAsync(_dbContext, onlyActive: true);

            RequestTypes = types
                .Where(type => BulkRequestStore.ResolveEffect(type).Kind
                            != BulkRequestStore.EffectKind.None)
                .ToList();

            TimedTypeIds = RequestTypes
                .Where(type => type.NeedsTime
                            || BulkRequestStore.ResolveEffect(type).Kind
                               == BulkRequestStore.EffectKind.ExitPermission)
                .Select(type => type.Id)
                .ToHashSet();
        }
        catch
        {
            RequestTypes = new();
            TimedTypeIds = new();
        }
    }

    /// <summary>
    /// تقديم طلبٍ نيابةً عن موظّفي اليوميات المحدَّدة، باعتمادٍ فوريّ بلا لجنة
    /// (قرار محمد 2026-08-07). المنطق كلّه بـ<see cref="BulkRequestStore"/>؛ هنا
    /// تفكيك التحديد وعرض النتيجة.
    /// </summary>
    public async Task<IActionResult> OnPostBulkRequestAsync()
    {
        var targets = ParseSelection(Request.Form["Selected"]);

        var options = new BulkRequestStore.Options(
            TypeId: int.TryParse(Request.Form["ReqTypeId"], out var typeId) ? typeId : 0,
            ShiftTypeId: int.TryParse(Request.Form["ReqShiftTypeId"], out var shiftId) ? shiftId : 0,
            FromTime: Request.Form["ReqFromTime"].ToString(),
            ToTime: Request.Form["ReqToTime"].ToString(),
            Reason: Request.Form["ReqReason"].ToString());

        var outcome = await BulkRequestStore.SubmitAsync(
            _dbContext, options, targets, User.Identity?.Name ?? "hr");

        TempData["SuccessMessage"] = outcome.Message;
        TempData["BulkSkips"] = outcome.Skips.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(outcome.Skips)
            : null;

        return RedirectToPage(new { Month, Search, StatusFilter, PageNumber });
    }

    /// <summary>
    /// قيم مربّعات التحديد <c>"{employeeId}|{yyyy-MM-dd}"</c> ⟵ أزواج موظف×يوم.
    /// نقيّة وثابتة: أي قيمة مشوَّهة تُهمَل بصمت بدل أن تُسقط الإرسال كلّه — التحديد
    /// يأتي من الشاشة، والقيمة الوحيدة الموثوقة منه هي ما يُفكّ صحيحاً.
    /// </summary>
    public static List<(int EmployeeId, DateOnly Date)> ParseSelection(IEnumerable<string?> values)
    {
        var targets = new List<(int, DateOnly)>();

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var parts = value.Split('|');
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var employeeId) || employeeId <= 0) continue;
            if (!DateOnly.TryParse(parts[1], out var date)) continue;
            targets.Add((employeeId, date));
        }

        return targets.Distinct().ToList();
    }

    public async Task<IActionResult> OnPostAnalyzeAsync()
    {
        var (year, month) = Period;
        var shiftTypeId = int.TryParse(Request.Form["ShiftTypeId"], out var id) ? id : 0;

        // بلا اختيار = تلقائي: كل موظف بمناوبته المسنَدة، وهذه احتياطُ غير المسنَدين
        // وحدهم. إجبار الاختيار كان يوهم أن التحليل يجري «على مناوبة» واحدة — وهو
        // لا يفعل: يقرأ التجاوز ثم الروستر ثم التعيين ثم الاستحقاق قبلها.
        var autoShift = shiftTypeId <= 0;
        if (autoShift) shiftTypeId = await DayAttendanceStore.ResolveDefaultShiftAsync(_dbContext);

        // الفحص المسبق يقول **لماذا** لم يُحلَّل شيء. بدونه كان المحرّك يعيد صفراً
        // صامتاً فتُقرأ الرسالة «تم التحديث — 0 يومية» كأنها نجاح، والشاشة فارغة.
        var blocker = await DayAttendanceStore.FindAnalyzeBlockerAsync(
            _dbContext, year, month, shiftTypeId);

        if (blocker is not null)
        {
            TempData["SuccessMessage"] = blocker;
        }
        else
        {
            // فترة الغلق (21 → 20) تعبر حدّ الشهر، والتحليل يُبنى شهراً تقويمياً.
            // بناء شهر التسمية وحده يترك نصف الفترة بلا يوميات — فنبني كل شهر تلمسه.
            var period = await ResolvePeriodAsync(year, month);
            var count = 0;

            foreach (var (coveredYear, coveredMonth) in period.CoveredMonths())
            {
                count += await DayAttendanceStore.AnalyzeMonthAsync(
                    _dbContext, coveredYear, coveredMonth, shiftTypeId);
            }

            var label = $"{period.From:yyyy-MM-dd} → {period.To:yyyy-MM-dd}";

            var scope = autoShift ? " (مناوبة كل موظف المسنَدة)" : "";

            TempData["SuccessMessage"] = count > 0
                ? $"تم تحديث الحضور — {count} يومية مولّدة للفترة {label}{scope}."
                : $"لم تُولَّد يوميات للفترة {label} — لا بصمات بها للموظفين المسنَدين " +
                  "لهذه المناوبة. تحقّق من إسناد الموظفين ومن نطاق تواريخ البصمات.";
        }
        return RedirectToPage(new { Month, Search, StatusFilter });
    }

    /// <summary>
    /// إشعار موظّفي **العرض الحالي** بملخّص حضورهم للفترة (نظير 🔔 بالحضور اليومي
    /// عند كيان — الفجوة #7 بدراسة 2026-08-07، وكانت مغلقةً بـ/AttendanceViewer وحده).
    ///
    /// <para>الفرق عن مؤلّف المستعرض مقصود: هناك النطاق يُبنى بخمسة فلاتر مستقلّة
    /// (قسم · فرع · مسمّى · عقد · جنسية)، وهنا النطاق **هو ما تراه بالشاشة**. فقيمة
    /// هذا الزرّ بالضبط أنّ الفلترة سبقته: تضغط «متأخر» فترى المتأخرين، ثم تُشعرهم
    /// وحدهم. تكرار مؤلّف النطاق هنا يُلغي هذه القيمة ويكرّر شاشةً قائمة.</para>
    ///
    /// <para>النوع مقصورٌ على «ملخّص الحضور»: النوعان الأسبوعيان يحتاجان رقم أسبوعٍ
    /// صريحاً لملء رموزهما ({2}/{3} بقالب الغياب الأسبوعي)، وفترة هذه الشاشة فترةُ
    /// غلقٍ شهرية قد تعبر حدّ الشهر — فتوليدهما هنا ينتج رسائل برقم أسبوع صفر.
    /// محلّهما المؤلّف الأسبوعي بـ/AttendanceViewer.</para>
    /// </summary>
    public async Task<IActionResult> OnPostNotifyAsync()
    {
        var (year, month) = Period;
        var form = Request.Form;

        var channel = form["Channel"].ToString() is { Length: > 0 } ch ? ch : "System";
        var template = form["Message"].ToString();
        var ccManager = form["CcManager"].ToString() is { Length: > 0 };

        var period = await ResolvePeriodAsync(year, month);

        // نفس القراءة والفلترة اللتين بنتا الشاشة — فالمُشعَرون هم المعروضون حرفياً.
        var rows = await DayAttendanceStore.ListRangeAsync(
            _dbContext, period.From, period.To, Search);

        var employeeIds = ApplyStatusFilter(rows)
            .Select(row => row.EmployeeId)
            .Distinct()
            .ToList();

        if (employeeIds.Count == 0)
        {
            TempData["SuccessMessage"] =
                "لا موظفين بالعرض الحالي — عدّل الفلتر أو شغّل «تحديث الحضور» أولاً.";
            return RedirectToPage(new { Month, Search, StatusFilter });
        }

        var (_, recipients, ccCount) = await AttendanceNotificationStore.SendAsync(
            _dbContext,
            type: "Summary",
            from: period.From,
            to: period.To,
            week: 0,
            channel: channel,
            template: template,
            employeeIds: employeeIds,
            ccMode: ccManager ? "Manager" : "None",
            ccEmployeeIds: Array.Empty<int>());

        // دفع فوري موازٍ للصادر — best-effort، وNo-Op حين القناة معطّلة. نظير
        // /AttendanceViewer: التطبيق المثبَّت يومض الإشعار بلا انتظار البريد.
        if (_push.IsEnabled)
        {
            var body = $"لديك إشعار حضور جديد ({period.From:MM-dd} → {period.To:MM-dd}). "
                     + "افتح البوابة للتفاصيل.";

            foreach (var employeeId in employeeIds)
            {
                await _push.SendToEmployeeAsync(
                    _dbContext,
                    employeeId,
                    new SmartAttendance.Web.Infrastructure.Notifications.PushPayload(
                        AttendanceNotificationStore.LabelOf("Summary"), body, "/EmployeePortal"));
            }
        }

        var channelLabel = AttendanceNotificationStore.Channels
            .FirstOrDefault(c => c.Key == channel).Label ?? channel;

        TempData["SuccessMessage"] =
            $"تم تأليف «ملخص الحضور» عبر {channelLabel} لـ{recipients} موظفاً"
            + (ccCount > 0 ? $" (+{ccCount} نسخة للمدير المباشر)" : "")
            + $" ({period.From:yyyy-MM-dd} → {period.To:yyyy-MM-dd}) — وُلّدت الرسائل بصندوق الصادر.";

        return RedirectToPage(new { Month, Search, StatusFilter });
    }
}
