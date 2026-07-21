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

    public static async Task EnsureAsync(ApplicationDbContext dbContext)
    {
        // زرع المفاتيح الناقصة فقط (idempotent) — إضافة حقل جديد للكتالوج لاحقاً تُزرع تلقائياً.
        var seedValues = new StringBuilder();
        foreach (var def in Catalog)
        {
            if (seedValues.Length > 0) seedValues.Append(", ");
            seedValues.Append("(N'").Append(def.Key).Append("', ").Append(def.DefaultRequired ? 1 : 0).Append(')');
        }

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            $"""
IF OBJECT_ID('HrFieldControls', 'U') IS NULL
BEGIN
    CREATE TABLE HrFieldControls
    (
        FieldKey nvarchar(64) NOT NULL PRIMARY KEY,
        IsRequired bit NOT NULL DEFAULT(0)
    );
END;

MERGE HrFieldControls AS target
USING (VALUES {seedValues}) AS source(FieldKey, IsRequired)
ON target.FieldKey = source.FieldKey
WHEN NOT MATCHED THEN INSERT (FieldKey, IsRequired) VALUES (source.FieldKey, source.IsRequired);
""");
    }

    /// <summary>مفاتيح الحقول الإلزامية حالياً (المقفلة دائماً ضمنها).</summary>
    public static async Task<HashSet<string>> GetRequiredKeysAsync(ApplicationDbContext dbContext)
    {
        await EnsureAsync(dbContext);
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT FieldKey FROM HrFieldControls WHERE IsRequired = 1;",
            command => { },
            reader => HrmsDatabase.GetString(reader, "FieldKey") ?? string.Empty);

        var keys = new HashSet<string>(rows.Where(k => k.Length > 0), StringComparer.Ordinal);
        foreach (var def in Catalog.Where(d => d.Locked))
        {
            keys.Add(def.Key);
        }
        return keys;
    }

    public static async Task SaveAsync(ApplicationDbContext dbContext, IReadOnlyCollection<string> requiredKeys)
    {
        await EnsureAsync(dbContext);

        // نبني قائمة القيم من الكتالوج حصراً (لا نثق بمفاتيح النموذج المرسل).
        var setValues = new StringBuilder();
        foreach (var def in Catalog)
        {
            var required = def.Locked || requiredKeys.Contains(def.Key);
            if (setValues.Length > 0) setValues.Append(", ");
            setValues.Append("(N'").Append(def.Key).Append("', ").Append(required ? 1 : 0).Append(')');
        }

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            $"""
UPDATE target
SET target.IsRequired = source.IsRequired
FROM HrFieldControls AS target
JOIN (VALUES {setValues}) AS source(FieldKey, IsRequired)
    ON target.FieldKey = source.FieldKey;
""");
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
