using System.Text;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// التحكم بالحقول (نمط كيان — قسم 16.6 بالدراسة): مفتاح «إلزامي» مركزي لكل حقل
/// بشاشة الموظف، يفرضه السيرفر بصفحتي الإنشاء والتعديل وتعلّمه الواجهة بنجمة.
/// الحقول الجوهرية (اسم/تعيين/فرع/قسم) مقفلة إلزامية دائماً ولا تُخزَّن قابلية تبديلها.
/// </summary>
public static class EmployeeFieldControl
{
    public sealed record FieldDef(string Key, string Label, string Group, bool Locked = false, bool DefaultRequired = false);

    /// <summary>الكتالوج بترتيب العرض؛ Key = اسم الخاصية بـ ViewModels الموظف (Create/Edit متطابقان).</summary>
    public static readonly IReadOnlyList<FieldDef> Catalog = new List<FieldDef>
    {
        // البيانات الأساسية
        new("EmployeeNo",     "كود الموظف",                 "البيانات الأساسية", Locked: true, DefaultRequired: true),
        new("FirstName",      "الاسم الأول (عربي)",          "البيانات الأساسية", Locked: true, DefaultRequired: true),
        new("SecondName",     "الاسم الثاني (عربي)",         "البيانات الأساسية"),
        new("ThirdName",      "الاسم الثالث (عربي)",         "البيانات الأساسية"),
        new("LastName",       "اللقب (عربي)",                "البيانات الأساسية", Locked: true, DefaultRequired: true),
        new("FirstNameEn",    "First Name",                  "البيانات الأساسية"),
        new("SecondNameEn",   "Second Name",                 "البيانات الأساسية"),
        new("ThirdNameEn",    "Third Name",                  "البيانات الأساسية"),
        new("LastNameEn",     "Last Name",                   "البيانات الأساسية"),
        new("NationalId",     "رقم الهوية",                  "البيانات الأساسية"),
        new("BirthDate",      "تاريخ الميلاد",               "البيانات الأساسية"),

        // الديموغرافيا
        new("Gender",         "الجنس",                       "الديموغرافيا"),
        new("MaritalStatus",  "الحالة الاجتماعية",           "الديموغرافيا"),
        new("Country",        "البلد",                       "الديموغرافيا"),
        new("Nationality",    "الجنسية",                     "الديموغرافيا"),
        new("Religion",       "الديانة",                     "الديموغرافيا"),
        new("MotherCountry",  "بلد الأم",                    "الديموغرافيا"),
        new("MotherCity",     "مدينة الأم",                  "الديموغرافيا"),

        // الوافدون
        new("PassportNo",     "رقم الجواز",                  "الوافدون"),
        new("SponsorName",    "اسم الكفيل",                  "الوافدون"),

        // التنظيم والتوظيف
        new("BranchId",       "موقع العمل (الفرع)",           "التنظيم والتوظيف", Locked: true, DefaultRequired: true),
        new("DepartmentId",   "القسم",                       "التنظيم والتوظيف", Locked: true, DefaultRequired: true),
        new("PositionId",     "المنصب",                      "التنظيم والتوظيف"),
        new("HireDate",       "تاريخ التعيين (العقد)",        "التنظيم والتوظيف", Locked: true, DefaultRequired: true),
        new("JoiningDate",    "تاريخ المباشرة الفعلية",       "التنظيم والتوظيف"),
        new("WorkType",       "نوع الدوام",                  "التنظيم والتوظيف"),
        new("JobGrade",       "الدرجة الوظيفية",             "التنظيم والتوظيف"),

        // التواصل
        new("Phone",          "رقم الهاتف",                  "التواصل"),
        new("PhoneExtension", "الامتداد الهاتفي",            "التواصل"),
        new("Email",          "البريد الإلكتروني (العمل)",    "التواصل"),
        new("PersonalEmail",  "البريد الشخصي",               "التواصل"),
    };

    /// <summary>إعدادات حقل واحد كما يديرها الأدمن من استوديو الحقول.</summary>
    public sealed class FieldSetting
    {
        public string Key { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public bool IsVisible { get; set; } = true;
        public string? CustomLabel { get; set; }
        public int DisplayOrder { get; set; }
    }

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        // زرع المفاتيح الناقصة فقط (idempotent) — إضافة حقل جديد للكتالوج لاحقاً تُزرع تلقائياً.
        var seedValues = new StringBuilder();
        var order = 0;
        foreach (var def in Catalog)
        {
            order++;
            if (seedValues.Length > 0) seedValues.Append(", ");
            seedValues.Append("(N'").Append(def.Key).Append("', ").Append(def.DefaultRequired ? 1 : 0)
                      .Append(", ").Append(order).Append(')');
        }

        // دفعة 1: الجدول والأعمدة — منفصلة عن دفعة الزرع لأن SQL يصرّف الدفعة كاملة
        // قبل تنفيذ ALTER فتفشل مراجع الأعمدة الجديدة بنفس الدفعة.
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            """
IF OBJECT_ID('HrFieldControls', 'U') IS NULL
BEGIN
    CREATE TABLE HrFieldControls
    (
        FieldKey nvarchar(64) NOT NULL PRIMARY KEY,
        IsRequired bit NOT NULL DEFAULT(0)
    );
END;

IF COL_LENGTH('HrFieldControls', 'IsVisible') IS NULL
    ALTER TABLE HrFieldControls ADD IsVisible bit NOT NULL CONSTRAINT DF_HrFieldControls_IsVisible DEFAULT(1);

IF COL_LENGTH('HrFieldControls', 'CustomLabel') IS NULL
    ALTER TABLE HrFieldControls ADD CustomLabel nvarchar(150) NULL;

IF COL_LENGTH('HrFieldControls', 'DisplayOrder') IS NULL
    ALTER TABLE HrFieldControls ADD DisplayOrder int NOT NULL CONSTRAINT DF_HrFieldControls_DisplayOrder DEFAULT(0);
""");

        // دفعة 2: زرع المفاتيح الناقصة وترميم الترتيب الصفري.
        await HrmsDatabase.ExecuteAsync(
            dbContext,
            $"""
MERGE HrFieldControls AS target
USING (VALUES {seedValues}) AS source(FieldKey, IsRequired, DisplayOrder)
ON target.FieldKey = source.FieldKey
WHEN NOT MATCHED THEN INSERT (FieldKey, IsRequired, DisplayOrder) VALUES (source.FieldKey, source.IsRequired, source.DisplayOrder);

-- ترميم ترتيب صفري (جداول أنشئت قبل عمود الترتيب)
UPDATE target
SET target.DisplayOrder = source.DisplayOrder
FROM HrFieldControls AS target
JOIN (VALUES {seedValues}) AS source(FieldKey, IsRequired, DisplayOrder)
    ON target.FieldKey = source.FieldKey
WHERE target.DisplayOrder = 0;
""");
    }

    /// <summary>كل إعدادات الحقول بترتيب العرض المخصص (المقفلة إلزامية دائماً).</summary>
    public static async Task<Dictionary<string, FieldSetting>> GetSettingsAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT FieldKey, IsRequired, IsVisible, CustomLabel, DisplayOrder FROM HrFieldControls;",
            command => { },
            reader => new FieldSetting
            {
                Key = HrmsDatabase.GetString(reader, "FieldKey"),
                IsRequired = HrmsDatabase.GetBool(reader, "IsRequired"),
                IsVisible = HrmsDatabase.GetBool(reader, "IsVisible"),
                CustomLabel = HrmsDatabase.GetString(reader, "CustomLabel") is { Length: > 0 } label ? label : null,
                DisplayOrder = HrmsDatabase.GetInt(reader, "DisplayOrder")
            });

        var map = rows.ToDictionary(r => r.Key, r => r, StringComparer.Ordinal);
        foreach (var def in Catalog.Where(d => d.Locked))
        {
            if (map.TryGetValue(def.Key, out var setting))
            {
                setting.IsRequired = true;
                setting.IsVisible = true; // الجوهرية لا تُخفى
            }
        }
        return map;
    }

    public static async Task SaveSettingsAsync(ApplicationDbContext dbContext, IReadOnlyList<FieldSetting> settings)
    {
        await EnsureAsync(dbContext);
        foreach (var setting in settings)
        {
            var def = Catalog.FirstOrDefault(d => d.Key == setting.Key);
            if (def == null) continue; // لا نثق بمفاتيح خارج الكتالوج

            var required = def.Locked || setting.IsRequired;
            var visible = def.Locked || setting.IsVisible;
            await HrmsDatabase.ExecuteAsync(
                dbContext,
                """
UPDATE HrFieldControls
SET IsRequired = @Required, IsVisible = @Visible, CustomLabel = @Label, DisplayOrder = @Order
WHERE FieldKey = @Key;
""",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Key", setting.Key);
                    HrmsDatabase.AddParameter(command, "@Required", required ? 1 : 0);
                    HrmsDatabase.AddParameter(command, "@Visible", visible ? 1 : 0);
                    HrmsDatabase.AddParameter(command, "@Label", string.IsNullOrWhiteSpace(setting.CustomLabel) ? DBNull.Value : setting.CustomLabel.Trim());
                    HrmsDatabase.AddParameter(command, "@Order", Math.Max(1, setting.DisplayOrder));
                });
        }
    }

    /// <summary>مفاتيح الحقول الإلزامية حالياً — المخفي لا يُفرض حتى لو معلَّم إلزامياً.</summary>
    public static async Task<HashSet<string>> GetRequiredKeysAsync(ApplicationDbContext dbContext)
        => RequiredKeys(await GetSettingsAsync(dbContext));

    /// <summary>اشتقاق مفاتيح الإلزامي من إعدادات محمّلة مسبقاً (يوفّر استعلاماً ثانياً).</summary>
    public static HashSet<string> RequiredKeys(Dictionary<string, FieldSetting> settings)
    {
        var keys = new HashSet<string>(
            settings.Values.Where(s => s.IsRequired && s.IsVisible).Select(s => s.Key),
            StringComparer.Ordinal);
        foreach (var def in Catalog.Where(d => d.Locked))
        {
            keys.Add(def.Key);
        }
        return keys;
    }

    /// <summary>
    /// الفرض بالسيرفر: يضيف أخطاء ModelState للحقول الإلزامية الفارغة.
    /// المقفلة تُستثنى (تفرضها DataAnnotations أصلاً)، والمفاتيح غير الموجودة
    /// بالـ ViewModel تُتجاهل بهدوء (مثل حقول Edit-فقط على نموذج Create).
    /// </summary>
    public static void ValidateRequired(object model, ISet<string> requiredKeys, ModelStateDictionary modelState, string prefix)
    {
        foreach (var def in Catalog)
        {
            if (def.Locked || !requiredKeys.Contains(def.Key)) continue;

            var property = model.GetType().GetProperty(def.Key);
            if (property == null) continue;

            var value = property.GetValue(model);
            var isEmpty = value switch
            {
                null => true,
                string text => string.IsNullOrWhiteSpace(text),
                int number => number <= 0,
                _ => false
            };

            if (isEmpty)
            {
                modelState.AddModelError($"{prefix}.{def.Key}", $"حقل «{def.Label}» مطلوب.");
            }
        }
    }
}
