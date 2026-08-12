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

    public static readonly IReadOnlyList<SensitiveField> Fields = new List<SensitiveField>
    {
        new(Salary, "الراتب والتعويضات"),
    };

    public static bool IsValid(string code) =>
        Fields.Any(f => f.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
}
