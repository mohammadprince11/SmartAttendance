using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// سياسات الطلبات المرتبطة بالحضور. كلها بيانات قابلة للتعديل من إعدادات الحضور،
/// ولا تعتمد الواجهة على افتراضات مخفية داخل JavaScript أو صفحة الموظف.
/// </summary>
public static class AttendanceRequestPolicy
{
    public const string ApplyEffectsKey = "Attendance.Requests.ApplyApprovedEffects";
    public const string CrossMidnightKey = "Attendance.Requests.AllowCrossMidnight";
    public const string TimePickerStepKey = "Attendance.Requests.TimePickerStepMinutes";

    public static async Task<bool> GetApplyEffectsAsync(ApplicationDbContext db) =>
        await HrSettingsStore.GetAsync(db, ApplyEffectsKey, "1") == "1";

    public static Task SetApplyEffectsAsync(ApplicationDbContext db, bool enabled) =>
        HrSettingsStore.SetAsync(db, ApplyEffectsKey, enabled ? "1" : "0");

    public static async Task<bool> GetCrossMidnightAsync(ApplicationDbContext db) =>
        await HrSettingsStore.GetAsync(db, CrossMidnightKey, "1") == "1";

    public static Task SetCrossMidnightAsync(ApplicationDbContext db, bool enabled) =>
        HrSettingsStore.SetAsync(db, CrossMidnightKey, enabled ? "1" : "0");

    public static async Task<int> GetTimePickerStepMinutesAsync(ApplicationDbContext db)
    {
        var raw = await HrSettingsStore.GetAsync(db, TimePickerStepKey, "30");
        return int.TryParse(raw, out var value) && value is 1 or 5 or 10 or 15 or 30 or 60 ? value : 30;
    }

    public static Task SetTimePickerStepMinutesAsync(ApplicationDbContext db, int minutes)
    {
        var safe = minutes is 1 or 5 or 10 or 15 or 30 or 60 ? minutes : 30;
        return HrSettingsStore.SetAsync(db, TimePickerStepKey, safe.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static TimeSpan Duration(TimeSpan start, TimeSpan end, bool allowCrossMidnight)
    {
        var duration = end - start;
        if (duration > TimeSpan.Zero) return duration;
        return allowCrossMidnight ? duration + TimeSpan.FromDays(1) : TimeSpan.Zero;
    }
}
