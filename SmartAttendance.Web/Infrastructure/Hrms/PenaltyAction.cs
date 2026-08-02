namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// أنواع الإجراء التأديبي — **مُعدَّدة لا نصّاً حرّاً**.
///
/// كان الجزاء عندنا نصّاً يكتبه المستخدم (<c>PenaltyAction</c>)، فلا يعرف النظام
/// أهو إنذار أم خصم أم إنهاء خدمة: النصّ **لا يقود شيئاً**. ودراسة كيان الحيّة
/// أظهرت أن النوع عندهم هو ما يفتح بقيّة الشاشة — الخصم يفتح حساباً مالياً،
/// وإنهاء الخدمة يفتح سبب إيقاف. فصار النوع هنا مفتاحاً يقود السلوك، وبقي النصّ
/// **عنواناً** للعرض والكتاب الرسمي.
///
/// ⚠️ المفاتيح نصوص ثابتة لا أرقام: صفوفٌ محفوظة بالقاعدة تبقى مقروءة لو أُعيد
/// ترتيب القائمة أو أُضيف نوع بينها.
/// </summary>
public static class PenaltyAction
{
    public const string VerbalWarning = "VerbalWarning";
    public const string WrittenWarning = "WrittenWarning";
    public const string Warning = "Warning";
    public const string FinalWarning = "FinalWarning";
    public const string SalaryDeduction = "SalaryDeduction";
    public const string RaiseDenial = "RaiseDenial";
    public const string Termination = "Termination";

    /// <summary>القائمة بترتيب التصعيد — من أخفّها إلى أشدّها.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Types = new[]
    {
        (VerbalWarning,   "تنبيه شفوي"),
        (WrittenWarning,  "تنبيه خطّي"),
        (Warning,         "إنذار"),
        (FinalWarning,    "إنذار نهائي"),
        (SalaryDeduction, "اقتطاع من الراتب"),
        (RaiseDenial,     "حرمان من زيادة الراتب"),
        (Termination,     "إنهاء الخدمة")
    };

    public static string Label(string? key) =>
        Types.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)).Label
        ?? "تنبيه شفوي";

    public static bool IsKnown(string? key) =>
        Types.Any(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>نوعٌ غير معروف يُعامَل كأخفّ الإجراءات — لا كخصمٍ بالخطأ.</summary>
    public static string Normalize(string? key) =>
        IsKnown(key) ? Types.First(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)).Key : VerbalWarning;

    /// <summary>هل يترتّب عليه حساب مالي؟ (وحده يفتح الوعاء والمقام).</summary>
    public static bool IsFinancial(string? key) =>
        string.Equals(Normalize(key), SalaryDeduction, StringComparison.Ordinal);

    /// <summary>هل ينهي خدمة الموظف؟ (وحده يطلب سبب إيقاف).</summary>
    public static bool IsTermination(string? key) =>
        string.Equals(Normalize(key), Termination, StringComparison.Ordinal);

    /// <summary>هل يحرم من الزيادة؟ يُستهلك عند احتساب الزيادات لا بالقسيمة.</summary>
    public static bool DeniesRaise(string? key) =>
        string.Equals(Normalize(key), RaiseDenial, StringComparison.Ordinal);

    /// <summary>
    /// ترجمة النوع إلى <c>FinancialImpactType</c> المستعمل بالقسيمة اليوم.
    ///
    /// غير الماليّ ⟹ <c>None</c> صراحةً: **إنذارٌ لا يخصم**. كان النصّ الحرّ يسمح
    /// بإنذارٍ مصحوبٍ بقيمة مالية بالخطأ فيُخصم بلا نوعِ إجراءٍ يبرّره.
    /// </summary>
    public static string FinancialImpact(string? key, string? declaredImpact) =>
        IsFinancial(key) ? (string.IsNullOrWhiteSpace(declaredImpact) ? "Days" : declaredImpact!) : "None";
}
