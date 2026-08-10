using System.Globalization;
using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قراءة/حفظ سياسة ربط الراتب بالحضور من <c>NexoraHrSettings</c>.
/// فُصلت عن <see cref="AttendanceSalaryLink"/> لتبقى تلك دالةً نقيّة قابلة
/// للاختبار بلا قاعدة بيانات.
/// </summary>
public static class AttendanceSalaryLinkSettings
{
    public static async Task<AttendanceSalaryLink.Policy> LoadAsync(ApplicationDbContext db)
    {
        var mode = await HrSettingsStore.GetAsync(db, AttendanceSalaryLink.ModeKey, AttendanceSalaryLink.Lenient);
        var absence = await HrSettingsStore.GetAsync(db, AttendanceSalaryLink.AbsenceFactorKey, "1");
        var negative = await HrSettingsStore.GetAsync(db, AttendanceSalaryLink.AllowNegativeKey, "0");
        // نفس مفتاح الأوفرتايم — مصدر واحد للساعات المعيارية (Issue 11).
        var hoursRaw = await HrSettingsStore.GetAsync(db, PayrollDivisorPolicy.StandardDailyHoursKey, "8");

        // قيمة تالفة بالإعداد لا يجوز أن تُغيّر رواتب: الرجوع لـ«يوم بيوم».
        if (!decimal.TryParse(absence, NumberStyles.Number, CultureInfo.InvariantCulture, out var factor) || factor < 0m)
            factor = 1m;

        var hours = PayrollDivisorPolicy.DailyHours(hoursRaw);

        // المقام (MonthlyDivisorDays) يُحسَم بالمسير من سياسة WorkingDays لا هنا.
        return new AttendanceSalaryLink.Policy(mode, factor, negative == "1", hours).Normalized();
    }

    public static async Task SaveAsync(ApplicationDbContext db, AttendanceSalaryLink.Policy policy)
    {
        var p = policy.Normalized();
        await HrSettingsStore.SetAsync(db, AttendanceSalaryLink.ModeKey, p.Mode);
        await HrSettingsStore.SetAsync(db, AttendanceSalaryLink.AbsenceFactorKey,
            p.AbsenceDeductionDays.ToString(CultureInfo.InvariantCulture));
        await HrSettingsStore.SetAsync(db, AttendanceSalaryLink.AllowNegativeKey, p.AllowNegative ? "1" : "0");
    }
}
