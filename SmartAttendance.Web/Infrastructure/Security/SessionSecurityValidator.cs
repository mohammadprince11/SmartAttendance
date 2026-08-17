namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>قرار فحص الجلسة/التوكن ضد الحالة الحالية للحساب.</summary>
public enum SessionSecurityDecision
{
    /// <summary>الجلسة سليمة — تمرّ كما هي.</summary>
    Valid = 0,

    /// <summary>الجلسة سليمة لكن مطالباتها بائتة — تُجدَّد التذكرة بالقيم الحالية.</summary>
    Refresh = 1,

    /// <summary>الجلسة/التوكن لم يعد صالحاً — يُسقط فوراً.</summary>
    Reject = 2
}

/// <summary>الحالة الحالية لحساب الدخول كما تُقرأ من قاعدة البيانات.</summary>
public sealed record AccountSecurityState(
    bool Exists,
    bool IsActive,
    bool IsLockedOut,
    string Role,
    string? SecurityStamp,
    bool MustChangePassword = false)
{
    public static readonly AccountSecurityState Missing =
        new(false, false, false, string.Empty, null);
}

/// <summary>
/// المرحلة 5 — قلب إبطال الجلسات البائتة، دالة نقية قابلة للاختبار بالكامل:
/// الكوكي/التوكن يحمل الدور والختم <b>كما كانا وقت الإصدار</b>، فنقارنهما بالحالة
/// الحالية للحساب. تغيير كلمة المرور/الدور/التعطيل/سحب الصلاحية يُبدّل الختم
/// سيرفرياً ⟹ كل تذكرة قديمة تُرفض بالطلب التالي.
///
/// سياسة الفشل: أي شكّ يُرفض (fail closed)، عدا حالة واحدة صريحة — تذكرة صادرة
/// قبل وجود الميزة (بلا ختم) لحساب حيّ وسليم تُجدَّد بدل أن تُسقط، حتى لا يُطرد
/// كل المستخدمين لحظة النشر.
/// </summary>
public static class SessionSecurityValidator
{
    public static SessionSecurityDecision Evaluate(
        string? ticketSecurityStamp,
        string? ticketRole,
        AccountSecurityState? account)
    {
        if (account is null || !account.Exists)
        {
            return SessionSecurityDecision.Reject;
        }

        if (!account.IsActive || account.IsLockedOut)
        {
            return SessionSecurityDecision.Reject;
        }

        var currentStamp = account.SecurityStamp;

        // الحساب بلا ختم بعد (قاعدة رُقّيت للتوّ): لا مرجع للمقارنة ⟹ نجدّد
        // لتُختم التذكرة، ولا نطرد المستخدم على غياب بيانات لم يتسبب بها.
        if (string.IsNullOrWhiteSpace(currentStamp))
        {
            return SessionSecurityDecision.Refresh;
        }

        if (string.IsNullOrWhiteSpace(ticketSecurityStamp))
        {
            return SessionSecurityDecision.Refresh;
        }

        if (!string.Equals(ticketSecurityStamp, currentStamp, StringComparison.Ordinal))
        {
            return SessionSecurityDecision.Reject;
        }

        // الختم مطابق لكن الدور تغيّر بلا تبديل ختم (مسار قديم): نُحدِّث المطالبات
        // بدل تركها بائتة — لا يوسّع الصلاحية لأن الفحص اللاحق يستعمل الدور الحالي.
        if (!string.Equals(
                (ticketRole ?? string.Empty).Trim(),
                (account.Role ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return SessionSecurityDecision.Refresh;
        }

        return SessionSecurityDecision.Valid;
    }
}
