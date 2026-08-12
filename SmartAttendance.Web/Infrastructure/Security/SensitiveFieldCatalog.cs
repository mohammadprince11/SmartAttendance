namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// كتالوج «الحقول الحساسة» لأدوار الوصول من النوع SensitiveFields. الدور يمنح رؤية
/// حقولٍ بعينها. الإنفاذ **إضافيّ**: يُضاف فوق الآليّات القائمة (إعداد
/// <c>Sensitive.SalaryRoles</c> ومنح <c>People.ViewCompensation</c>) فلا يكسر شيئاً —
/// يوسّع الرؤية فقط لمن يملك الدور، ولا يسحبها ممّن يملكها اليوم.
///
/// نبدأ بحقلٍ واحدٍ مُنفَّذٍ كاملاً (الراتب) تجنّباً لأي «وعدٍ كاذب»؛ تُضاف حقولٌ أخرى
/// (آيبان/هوية/جواز) عند ربط إنفاذها بمواضع عرضها.
/// </summary>
public static class SensitiveFieldCatalog
{
    public const string Salary = "Salary";

    public sealed record SensitiveField(string Code, string Label);

    // ملاحظة: الإنفاذ عندنا على مستوى البطاقة لا الحقل المفرد — منح «الراتب» يكشف
    // بطاقة الراتب وبطاقة البيانات المالية (البنك · الآيبان · الضمان · الضريبة) معاً،
    // إذ كلتاهما محجوبتان بنفس CanViewSalary. لذا مدخلٌ واحدٌ دقيق بدل حقولٍ توحي
    // بمنحٍ مستقلٍّ لا يدعمه الإنفاذ الحاليّ. الفصل حقلاً-بحقل (نمط كيان) يتطلّب
    // تقسيم البطاقة المالية أولاً — مرحلةٌ لاحقة.
    public static readonly IReadOnlyList<SensitiveField> Fields = new List<SensitiveField>
    {
        new(Salary, "الراتب والبيانات المالية (البنك · الآيبان · الضمان · الضريبة)"),
    };

    public static bool IsValid(string code) =>
        Fields.Any(f => f.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
}
