using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// مراقبة تغيير إعدادات الرواتب — نظير «الخيارات الأمنية: إرسال إشعار عند تغيير
/// إعدادات الرواتب → أبلغ المشرف الأعلى على التغييرات من نوع (27)» بتهيئة كيان.
///
/// كل حفظ لمفتاح <c>Payroll.*</c> يمرّ من <see cref="SetAndTrackAsync"/>: يُقرأ القديم،
/// يُكتب الجديد، وإن اختلفا يُسجَّل بـ<c>AuditLogs</c> (قبل/بعد/الفاعل/IP) ويُطلق إشعار
/// جرس للدور المستهدف. **التسجيل دائم؛ الإشعار بمفتاح تفعيل** (افتراضياً معطَّل كي لا
/// يُغرق الجرس بضبطٍ أولي).
///
/// لماذا يهمّ: خفض نسبة الضمان أو تغيير مقام الأيام قبل مسير واحد يغيّر رواتب الجميع
/// بصمت — التدقيق بعد الواقعة يجد الأثر لا الفاعل، والإشعار الفوري يجد كليهما.
/// </summary>
public static class PayrollConfigChangeMonitor
{
    public const string KeyEnabled = "Payroll.ConfigMonitor.NotifyEnabled";
    public const string KeyTargetRole = "Payroll.ConfigMonitor.TargetRole";
    public const string DefaultTargetRole = "Admin";

    /// <summary>
    /// تسميات مقروءة للمفاتيح المراقَبة — تظهر بالإشعار وسجل التدقيق بدل المفتاح التقني.
    /// غير المذكور يُعرض بمفتاحه كما هو (يبقى مراقَباً).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Payroll.GosiTaxBase"] = "وعاء الضمان/الضريبة",
        ["Payroll.OvertimeBaseMode"] = "وعاء الأوفرتايم",
        ["Payroll.UnpaidLeaveBaseMode"] = "وعاء الإجازة غير المدفوعة",
        [PayrollDivisorPolicy.SalaryDaysBasisKey] = "مقام أيام الراتب",
        [PayrollDivisorPolicy.StandardDailyHoursKey] = "الساعات المعيارية لليوم",
        [PayrollCapsPolicy.KeyDeductionCapAmount] = "سقف الاقتطاع الشهري (مبلغ)",
        [PayrollCapsPolicy.KeyDeductionCapPercent] = "سقف الاقتطاع الشهري (%)",
        [PayrollCapsPolicy.KeyOvertimeCapAmount] = "سقف الإضافي الشهري (مبلغ)",
        [PayrollCapsPolicy.KeyOvertimeCapHours] = "سقف الإضافي الشهري (ساعات)",
        [PayrollRunStore.KeyRequireCommitteeApproval] = "اشتراط اعتماد اللجنة للإصدار",
        [KeyEnabled] = "مراقبة تغيير الإعدادات (تفعيل)",
        [KeyTargetRole] = "مراقبة تغيير الإعدادات (الجهة)",
    };

    public static string LabelOf(string key) => Labels.TryGetValue(key, out var l) ? l : key;

    /// <summary>
    /// يحفظ إعداد رواتب **ويتتبّع** التغيير. يعيد true إن تغيّرت القيمة فعلاً.
    /// الحفظ نفسه عبر <see cref="HrSettingsStore.SetAsync"/> — لا مسار كتابة ثانٍ.
    /// </summary>
    public static async Task<bool> SetAndTrackAsync(
        ApplicationDbContext db, string key, string? newValue, string actor, string? ipAddress)
    {
        var oldValue = await HrSettingsStore.GetAsync(db, key, string.Empty);
        var normalizedNew = newValue ?? string.Empty;

        await HrSettingsStore.SetAsync(db, key, normalizedNew);

        if (string.Equals(oldValue, normalizedNew, StringComparison.Ordinal))
            return false;

        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO AuditLogs (EntityName, EntityId, Action, OldValues, NewValues, UserName, IpAddress)
VALUES ('PayrollConfig', @Key, 'Change Payroll Setting', @Old, @New, @Actor, @Ip);
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Key", key);
                HrmsDatabase.AddParameter(command, "@Old", HrmsDatabase.JsonLine(("Value", oldValue)));
                HrmsDatabase.AddParameter(command, "@New", HrmsDatabase.JsonLine(("Value", normalizedNew)));
                HrmsDatabase.AddParameter(command, "@Actor", actor);
                HrmsDatabase.AddParameter(command, "@Ip", (object?)ipAddress ?? DBNull.Value);
            });

        var notify = bool.TryParse(await HrSettingsStore.GetAsync(db, KeyEnabled, "False"), out var on) && on;
        if (!notify)
            return true;

        var role = await HrSettingsStore.GetAsync(db, KeyTargetRole, DefaultTargetRole);
        var label = LabelOf(key);
        var oldShown = string.IsNullOrEmpty(oldValue) ? "—" : oldValue;

        await HrmsDatabase.ExecuteAsync(
            db,
            """
INSERT INTO SystemNotifications (Title, Message, TargetRole, Url)
VALUES (N'⚙️ تغيير بإعدادات الرواتب', @Message, @Role, '/Payroll/Settings');
""",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Message",
                    $"غيّر {actor} «{label}» من ({oldShown}) إلى ({normalizedNew}). راجع قبل المسير القادم.");
                HrmsDatabase.AddParameter(command, "@Role", string.IsNullOrWhiteSpace(role) ? DefaultTargetRole : role.Trim());
            });

        return true;
    }
}
