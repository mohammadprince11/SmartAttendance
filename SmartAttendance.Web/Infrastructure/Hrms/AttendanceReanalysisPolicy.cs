using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// سياسة إعادة تحليل الحضور بعد الموافقات (نمط كيان — «التهيئة» بإعدادات تسجيل الحضور،
/// مفتاحان متقابلان):
/// <list type="number">
/// <item><b>إعادة تحليل تلقائية</b> بمجرد الموافقة على إجازة/مغادرة/بصمة مفقودة، فلا
/// يحتاج المستخدم تشغيل «تحديث الحضور» يدوياً ليرى أثر الموافقة.</item>
/// <item><b>حارس مضاد</b>: امنع إعادة التحليل إذا كانت هناك إجراءات **منفَّذة** على
/// حضور الموظف بذلك اليوم (قضية مخالفة أو حركة مالية مرتبطة باقتراح معتمَد/تلقائي)،
/// وإلا دهست إعادةُ التحليل قراراً بشرياً أو أثراً مالياً سبق ترحيله.</item>
/// </list>
/// الافتراض: المفتاح الأول <b>معطّل</b> والثاني <b>مفعّل</b> — أي سلوك اليوم يبقى كما هو
/// حتى يفعّله محمد صراحةً، والحماية قائمة منذ اللحظة الأولى.
/// </summary>
public static class AttendanceReanalysisPolicy
{
    public const string AutoReanalyzeKey = "Attendance.Reanalysis.AutoOnApproval";
    public const string GuardExecutedKey = "Attendance.Reanalysis.GuardExecutedActions";

    public static async Task<bool> GetAutoReanalyzeAsync(ApplicationDbContext db) =>
        await HrSettingsStore.GetAsync(db, AutoReanalyzeKey, "0") == "1";

    public static Task SetAutoReanalyzeAsync(ApplicationDbContext db, bool enabled) =>
        HrSettingsStore.SetAsync(db, AutoReanalyzeKey, enabled ? "1" : "0");

    public static async Task<bool> GetGuardExecutedAsync(ApplicationDbContext db) =>
        await HrSettingsStore.GetAsync(db, GuardExecutedKey, "1") == "1";

    public static Task SetGuardExecutedAsync(ApplicationDbContext db, bool enabled) =>
        HrSettingsStore.SetAsync(db, GuardExecutedKey, enabled ? "1" : "0");

    /// <summary>نتيجة محاولة إعادة التحليل — تُعرض كذيل لرسالة الموافقة.</summary>
    public readonly record struct Outcome(bool Ran, string Message)
    {
        public static readonly Outcome Disabled =
            new(false, "شغّل «تحديث الحضور» لتظهر باليومية.");

        public static readonly Outcome Blocked =
            new(false, "لم يُعَد التحليل تلقائياً: على هذا اليوم إجراءات منفَّذة (حارس الحماية مفعّل).");
    }

    /// <summary>
    /// هل على (موظف × يوم) إجراءٌ نُفِّذ فعلاً؟ أي اقتراح معتمَد أو تلقائي أنشأ قضية
    /// مخالفة أو حركة مالية. الاقتراح المعلّق أو المتجاهَل لا يمنع — لم يُنفَّذ شيء بعد.
    /// </summary>
    public static async Task<bool> HasExecutedActionsAsync(
        ApplicationDbContext db, int employeeId, DateOnly date)
    {
        await RecommendationStore.EnsureAsync(db);
        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
SELECT COUNT(1) FROM AttendanceRecommendations
WHERE EmployeeId = @Employee AND WorkDate = @Date
  AND Status IN (N'Approved', N'Auto')
  AND (ViolationCaseId IS NOT NULL OR TransactionId IS NOT NULL);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Employee", employeeId);
                HrmsDatabase.AddParameter(command, "@Date", date.ToDateTime(TimeOnly.MinValue));
            }) > 0;
    }

    /// <summary>
    /// يُستدعى بعد كل موافقة تمسّ يوميةً (بصمة مفقودة · إجازة · مغادرة). يعيد بناء
    /// يوميات الشهر الذي يقع فيه <paramref name="date"/> — والعملية idempotent فتكرارها
    /// غير ضارّ.
    /// </summary>
    public static async Task<Outcome> AfterApprovalAsync(
        ApplicationDbContext db, int employeeId, DateOnly date)
    {
        if (!await GetAutoReanalyzeAsync(db)) return Outcome.Disabled;

        if (await GetGuardExecutedAsync(db) && await HasExecutedActionsAsync(db, employeeId, date))
            return Outcome.Blocked;

        var shiftTypeId = await ResolveShiftTypeAsync(db, employeeId);
        if (shiftTypeId == 0)
            return new Outcome(false, "لم يُعَد التحليل: لا مناوبة مسنَدة ولا مناوبة افتراضية.");

        var count = await DayAttendanceStore.AnalyzeMonthAsync(db, date.Year, date.Month, shiftTypeId);
        return new Outcome(true, $"وأُعيد تحليل الحضور تلقائياً ({count} يومية).");
    }

    /// <summary>
    /// خطّاف مسار الاعتماد المركزي (نظير <c>ApplyIfFinancialAsync</c>): إن كان الطلب
    /// المعتمَد من نوع يمسّ اليومية (إجازة أو مغادرة) فأعد تحليل شهر تاريخ بدايته.
    /// يتجاهل ما عداه، وidempotent (إعادة التحليل نفسها idempotent).
    /// </summary>
    public static async Task<Outcome> ApplyIfAttendanceAffectingAsync(
        ApplicationDbContext db, int requestId)
    {
        var row = (await HrmsDatabase.QueryAsync(
            db,
            """
SELECT TOP 1 EmployeeId, FromDate
FROM SelfServiceRequests
WHERE Id = @Request AND Status = N'Approved'
  AND RequestType IN (N'Leave', N'Permission')
  AND EmployeeId IS NOT NULL AND FromDate IS NOT NULL;
""",
            command => HrmsDatabase.AddParameter(command, "@Request", requestId),
            reader => (
                EmployeeId: HrmsDatabase.GetInt(reader, "EmployeeId"),
                FromDate: HrmsDatabase.GetDateOnly(reader, "FromDate")))).FirstOrDefault();

        if (row.EmployeeId <= 0 || row.FromDate is not { } date)
            return new Outcome(false, string.Empty);

        return await AfterApprovalAsync(db, row.EmployeeId, date);
    }

    /// <summary>مناوبة الموظف المسنَدة، وإلا أول مناوبة فعّالة كافتراضية للتحليل.</summary>
    private static async Task<int> ResolveShiftTypeAsync(ApplicationDbContext db, int employeeId)
    {
        var assignments = await EmployeeShiftTypeStore.MapAsync(db);
        if (assignments.TryGetValue(employeeId, out var assigned) && assigned > 0) return assigned;

        var shifts = await ShiftTypeStore.ListAsync(db);
        return shifts.FirstOrDefault(s => s.IsActive)?.Id ?? shifts.FirstOrDefault()?.Id ?? 0;
    }
}
