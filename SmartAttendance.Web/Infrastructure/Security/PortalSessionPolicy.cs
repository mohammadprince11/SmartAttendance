using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.HrSettings;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// سياسة جلسة بوابة الموظف (حاجز إعارة الهاتف): موظف يدخل ثم يسلّم هاتفه لزميل —
/// الجلسة تنتهي تلقائياً بعد خمولٍ قابلٍ للضبط (إنفاذ سيرفري عبر تذكرة الكوكي، لا
/// يُخدع بتعطيل جافاسكربت) وعند مغادرة التطبيق/إطفاء الشاشة مدةً تتجاوز السماح
/// (طبقة عميل). الصفر يعطّل أي قاعدة. المنطق الزمني نقي قابل للاختبار.
/// </summary>
public static class PortalSessionPolicy
{
    /// <summary>دقائق الخمول قبل الخروج التلقائي (0 = معطّل). إنفاذ سيرفري.</summary>
    public const string IdleMinutesKey = "EmployeePortal.IdleLogoutMinutes";

    /// <summary>ثواني السماح بعد مغادرة التطبيق/إطفاء الشاشة قبل الخروج (0 = معطّل). طبقة عميل.</summary>
    public const string LeaveSecondsKey = "EmployeePortal.LeaveLogoutSeconds";

    public const int DefaultIdleMinutes = 30;
    public const int DefaultLeaveSeconds = 60;

    /// <summary>مفتاح «آخر نشاط» داخل خصائص تذكرة الكوكي المشفّرة (لا يعبث به العميل).</summary>
    public const string LastActivityItem = "laUtc";

    /// <summary>لا نعيد إصدار التذكرة بكل طلب — تجديد كل دقيقة يكفي دقةً ويوفّر الكتابة.</summary>
    public static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(60);

    /// <summary>هل انقضت مهلة الخمول؟ نقية: 0 = القاعدة معطّلة.</summary>
    public static bool ShouldExpire(DateTime lastActivityUtc, DateTime nowUtc, int idleMinutes) =>
        idleMinutes > 0 && nowUtc - lastActivityUtc > TimeSpan.FromMinutes(idleMinutes);

    /// <summary>هل حان تجديد ختم «آخر نشاط» بالتذكرة؟</summary>
    public static bool ShouldRenew(DateTime lastActivityUtc, DateTime nowUtc) =>
        nowUtc - lastActivityUtc >= RenewInterval;

    /// <summary>قراءة آمنة لقيمة إعداد عددية (سالب/تالف ⟵ الافتراضي، وسقف يمنع العبث).</summary>
    public static int ParseSetting(string? raw, int fallback, int max) =>
        int.TryParse(raw, out var v) && v >= 0 ? Math.Min(v, max) : fallback;

    public static async Task<int> GetIdleMinutesAsync(ApplicationDbContext db) =>
        ParseSetting(await HrSettingsStore.GetAsync(db, IdleMinutesKey, DefaultIdleMinutes.ToString()),
            DefaultIdleMinutes, max: 1440);

    public static async Task<int> GetLeaveSecondsAsync(ApplicationDbContext db) =>
        ParseSetting(await HrSettingsStore.GetAsync(db, LeaveSecondsKey, DefaultLeaveSeconds.ToString()),
            DefaultLeaveSeconds, max: 3600);

    public static Task SetIdleMinutesAsync(ApplicationDbContext db, int minutes) =>
        HrSettingsStore.SetAsync(db, IdleMinutesKey, Math.Clamp(minutes, 0, 1440).ToString());

    public static Task SetLeaveSecondsAsync(ApplicationDbContext db, int seconds) =>
        HrSettingsStore.SetAsync(db, LeaveSecondsKey, Math.Clamp(seconds, 0, 3600).ToString());
}
