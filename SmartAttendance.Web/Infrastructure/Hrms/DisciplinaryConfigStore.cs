using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Pages.DisciplinaryRules;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قراءة تهيئة المخالفات وقوالب رسائلها — **مصدر واحد لشاشتين**.
///
/// تحتاجها اليوم شاشتان: `/DisciplinaryRules` (حيث تُحرَّر) و`/Violations` (حيث
/// تُعرض تبويباً بجانب الحالات والإجراءات). ونسخُ الاستعلامات بينهما كان سيصنع
/// **مصدرَي حقيقة لنفس الصفوف**: يتغيّر أحدهما ويبقى الآخر — وهو بالضبط نمط
/// العطل الذي عالجناه بالتوكنات والقائمة. فالقراءة هنا، مرّةً واحدة.
///
/// ⚠️ **قراءة فقط**. الكتابة (عشرون معالجاً: حفظ التهيئة · رفع الفورمة · القوالب
/// · الطبقات) تبقى بنموذج `/DisciplinaryRules` وحده، وتُرسل إليه النماذج بـ
/// `asp-page` حتى من شاشة المخالفات. منطقٌ يكتب سلّم الجزاءات وأثره المالي لا
/// يُنسَخ لأجل عرضٍ ثانٍ.
/// </summary>
public static class DisciplinaryConfigStore
{
    /// <summary>ما تحتاجه تبويبتا «التهيئة» و«القوالب والرسائل» للعرض.</summary>
    public sealed class ConfigView
    {
        public DisciplinarySettings Settings { get; init; } = new();

        public List<TemplateType> TemplateTypes { get; init; } = new();

        public List<MessageTemplate> MessageTemplates { get; init; } = new();
    }

    public static async Task<ConfigView> LoadAsync(ApplicationDbContext db)
    {
        await DisciplinarySchema.EnsureAsync(db);

        return new ConfigView
        {
            Settings = await LoadSettingsAsync(db),
            TemplateTypes = await LoadTemplateTypesAsync(db),
            MessageTemplates = await LoadMessageTemplatesAsync(db)
        };
    }

    public static async Task<DisciplinarySettings> LoadSettingsAsync(ApplicationDbContext db)
    {
        var items = await HrmsDatabase.QueryAsync(
            db,
            "SELECT [Key], ISNULL([Value], '') AS [Value] FROM DisciplinarySettings;",
            command => { },
            reader => new KeyValuePair<string, string>(
                HrmsDatabase.GetString(reader, "Key"),
                HrmsDatabase.GetString(reader, "Value")));

        var map = items.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);

        return new DisciplinarySettings
        {
            RequiresCommitteeApproval = Bool(map, "RequiresCommitteeApproval"),
            AllowEmployeeAppeal = Bool(map, "AllowEmployeeAppeal", true),
            AppealWindowDays = Int(map, "AppealWindowDays", 3),
            DefaultTemplateType = Text(map, "DefaultTemplateType", "PenaltyNotice"),
            ApprovingAuthorityName = Text(map, "ApprovingAuthorityName", "قسم الموارد البشرية"),
            DocumentNumberFormat = Text(map, "DocumentNumberFormat", "DISC-{yyyy}-{0000}"),
            FormHeader = Text(map, "FormHeader", string.Empty),
            FormFooter = Text(map, "FormFooter", string.Empty),
            HeaderImagePath = Text(map, "HeaderImagePath", string.Empty),
            FooterImagePath = Text(map, "FooterImagePath", string.Empty),
            A4FormFilePath = Text(map, "A4FormFilePath", string.Empty),
            A4FormFileType = Text(map, "A4FormFileType", string.Empty),
            MainBodyText = Text(map, "MainBodyText", string.Empty),
            MainBodyXPercent = Decimal(map, "MainBodyXPercent", 8),
            MainBodyYPercent = Decimal(map, "MainBodyYPercent", 8),
            MainBodyWidthPercent = Decimal(map, "MainBodyWidthPercent", 84),
            MainBodyFontFamily = Text(map, "MainBodyFontFamily", "Tahoma"),
            MainBodyFontSize = Int(map, "MainBodyFontSize", 13),
            MainBodyFontColor = Text(map, "MainBodyFontColor", "#0b1d31"),
            MainBodyIsBold = Bool(map, "MainBodyIsBold", true),
            MainBodyTextAlign = Text(map, "MainBodyTextAlign", "right")
        };
    }

    public static async Task<List<TemplateType>> LoadTemplateTypesAsync(ApplicationDbContext db) =>
        await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, Name, Code, ISNULL(Description, '') AS Description, IsActive, CreatedAt
FROM DisciplinaryTemplateTypes
ORDER BY Name;
""",
            command => { },
            reader => new TemplateType
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                Code = HrmsDatabase.GetString(reader, "Code"),
                Description = HrmsDatabase.GetString(reader, "Description"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });

    public static async Task<List<MessageTemplate>> LoadMessageTemplatesAsync(ApplicationDbContext db) =>
        await HrmsDatabase.QueryAsync(
            db,
            """
SELECT Id, Name, TemplateType, Subject, Body, IsDefault, IsActive, CreatedAt
FROM DisciplinaryMessageTemplates
ORDER BY IsDefault DESC, Name;
""",
            command => { },
            reader => new MessageTemplate
            {
                Id = HrmsDatabase.GetInt(reader, "Id"),
                Name = HrmsDatabase.GetString(reader, "Name"),
                TemplateType = HrmsDatabase.GetString(reader, "TemplateType"),
                Subject = HrmsDatabase.GetString(reader, "Subject"),
                Body = HrmsDatabase.GetString(reader, "Body"),
                IsDefault = HrmsDatabase.GetBool(reader, "IsDefault"),
                IsActive = HrmsDatabase.GetBool(reader, "IsActive"),
                CreatedAt = HrmsDatabase.GetDateTime(reader, "CreatedAt")
            });

    private static bool Bool(Dictionary<string, string> map, string key, bool fallback = false) =>
        map.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int Int(Dictionary<string, string> map, string key, int fallback) =>
        map.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    private static decimal Decimal(Dictionary<string, string> map, string key, decimal fallback) =>
        map.TryGetValue(key, out var value) && decimal.TryParse(value, out var parsed) ? parsed : fallback;

    private static string Text(Dictionary<string, string> map, string key, string fallback) =>
        map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}
