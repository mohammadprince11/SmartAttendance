namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قاعدة اشتقاق المواطنة من الجنسية — نظير تهيئة كيان
/// «تعيين الموظف كمواطن عندما تكون الجنسية …».
///
/// القاعدة بيانات لا كود: قائمة جنسيات بمفتاح إعدادات، والمطابقة بعد تطبيع
/// المسافات وأداة التعريف كي لا تفرّق «سعودي» عن «السعودي » المكتوبة بمسافة زائدة.
/// </summary>
public static class CitizenshipPolicy
{
    public const string KeyEnabled = "People.Citizenship.Enabled";
    public const string KeyNationalities = "People.Citizenship.Nationalities";

    /// <summary>الافتراضي يطابق سلوك كيان الموثّق بدراسة 2026-08-15.</summary>
    public const string DefaultNationalities = "سعودي";

    /// <summary>يفكّ قائمة الجنسيات المخزّنة — الفاصلتان العربية واللاتينية سواء.</summary>
    public static IReadOnlyList<string> Parse(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return Array.Empty<string>();

        return configured
            .Split(new[] { ',', '،', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>هل تجعل هذه الجنسيةُ الموظفَ مواطناً وفق القائمة المهيّأة؟</summary>
    public static bool IsCitizen(string? nationality, string? configured)
    {
        var target = Normalize(nationality);
        if (target.Length == 0)
            return false;

        return Parse(configured).Contains(target, StringComparer.Ordinal);
    }

    /// <summary>تطبيع للمقارنة: قصّ المسافات وإسقاط «ال» التعريف الأولى إن وُجدت.</summary>
    private static string Normalize(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.StartsWith("ال", StringComparison.Ordinal) && trimmed.Length > 2
            ? trimmed[2..]
            : trimmed;
    }
}
