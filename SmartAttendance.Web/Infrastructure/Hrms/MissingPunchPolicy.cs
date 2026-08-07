using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// حدود طلبات البصمة المفقودة (نمط كيان — «التهيئة» بإعدادات تسجيل الحضور): ثلاثة
/// قيود تمنع تحوّل الطلب من استثناءٍ إلى بابٍ خلفيّ للحضور.
///
/// <list type="bullet">
/// <item><b>سقف شهري</b>: عدد الطلبات المسموحة للموظف بالشهر (0 = بلا سقف).</item>
/// <item><b>نافذة زمنية</b>: كم يوماً للخلف يُقبل الطلب (0 = بلا حدّ).</item>
/// <item><b>سبب إلزامي</b>: لا طلب بلا تبرير مكتوب.</item>
/// </list>
///
/// <para>كل القيم <b>معطّلة افتراضياً</b> (0 / لا) — سلوك ما قبل الميزة يبقى كما هو
/// حتى يضبطها محمد، فلا يُرفض طلبٌ كان يمرّ أمس.</para>
///
/// <para>القيود تُفرَض بـ<see cref="ValidateAsync"/> عند الإنشاء فقط: التحرير أو
/// البتّ لا يُعاد تقييمه، لأن الطلب القائم قُدّم تحت سياسةٍ ربما كانت مختلفة.</para>
/// </summary>
public static class MissingPunchPolicy
{
    public const string MonthlyLimitKey = "Attendance.MissingPunch.MonthlyLimit";
    public const string WindowDaysKey = "Attendance.MissingPunch.WindowDays";
    public const string ReasonRequiredKey = "Attendance.MissingPunch.ReasonRequired";

    public static async Task<int> GetMonthlyLimitAsync(ApplicationDbContext db) =>
        int.TryParse(await HrSettingsStore.GetAsync(db, MonthlyLimitKey, "0"), out var v) && v > 0 ? v : 0;

    public static Task SetMonthlyLimitAsync(ApplicationDbContext db, int value) =>
        HrSettingsStore.SetAsync(db, MonthlyLimitKey, Math.Max(0, value).ToString());

    public static async Task<int> GetWindowDaysAsync(ApplicationDbContext db) =>
        int.TryParse(await HrSettingsStore.GetAsync(db, WindowDaysKey, "0"), out var v) && v > 0 ? v : 0;

    public static Task SetWindowDaysAsync(ApplicationDbContext db, int value) =>
        HrSettingsStore.SetAsync(db, WindowDaysKey, Math.Max(0, value).ToString());

    public static async Task<bool> GetReasonRequiredAsync(ApplicationDbContext db) =>
        await HrSettingsStore.GetAsync(db, ReasonRequiredKey, "0") == "1";

    public static Task SetReasonRequiredAsync(ApplicationDbContext db, bool enabled) =>
        HrSettingsStore.SetAsync(db, ReasonRequiredKey, enabled ? "1" : "0");

    /// <summary>
    /// قرار قبول النافذة الزمنية — دالة نقية. <paramref name="windowDays"/> = 0 يعني
    /// بلا حدّ. البصمة **المستقبلية** مقبولة دائماً (لا معنى لنافذةٍ للأمام).
    /// </summary>
    public static bool IsWithinWindow(DateOnly punchDate, DateOnly today, int windowDays)
    {
        if (windowDays <= 0) return true;
        if (punchDate >= today) return true;
        return today.DayNumber - punchDate.DayNumber <= windowDays;
    }

    /// <summary>
    /// فحص الحدود الثلاثة قبل إنشاء طلب جديد.
    /// </summary>
    /// <returns>رسالة الرفض، أو <c>null</c> إن كان الطلب مقبولاً.</returns>
    public static async Task<string?> ValidateAsync(
        ApplicationDbContext db, int employeeId, DateTime punchAt, string? reason)
    {
        if (await GetReasonRequiredAsync(db) && string.IsNullOrWhiteSpace(reason))
            return "السبب إلزامي لطلب البصمة المفقودة.";

        var windowDays = await GetWindowDaysAsync(db);
        var punchDate = DateOnly.FromDateTime(punchAt);
        if (!IsWithinWindow(punchDate, DateOnly.FromDateTime(DateTime.Today), windowDays))
            return $"لا يُقبل طلب بصمة أقدم من {windowDays} يوماً.";

        var limit = await GetMonthlyLimitAsync(db);
        if (limit > 0)
        {
            var used = await CountInMonthAsync(db, employeeId, punchAt.Year, punchAt.Month);
            if (used >= limit)
                return $"تجاوز الحدّ الشهري لطلبات البصمة المفقودة ({limit} طلبات) — المستهلك {used}.";
        }

        return null;
    }

    /// <summary>
    /// عدد طلبات الموظف بشهر البصمة، **مستثنياً المرفوض والملغى** — الطلب الذي رُفض
    /// لم يمنح الموظف شيئاً، فاحتسابه ضمن السقف عقوبة مزدوجة.
    /// </summary>
    public static async Task<int> CountInMonthAsync(
        ApplicationDbContext db, int employeeId, int year, int month)
    {
        var from = new DateTime(year, month, 1);
        var to = from.AddMonths(1);

        return await HrmsDatabase.ScalarAsync<int>(
            db,
            """
SELECT COUNT(1) FROM MissingPunchRequests
WHERE EmployeeId = @Employee
  AND PunchAt >= @From AND PunchAt < @To
  AND Status NOT IN (N'Rejected', N'Cancelled');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Employee", employeeId);
                HrmsDatabase.AddParameter(command, "@From", from);
                HrmsDatabase.AddParameter(command, "@To", to);
            });
    }
}
