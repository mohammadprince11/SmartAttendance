using System.Text.Json;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// يغذّي باني الشروط بالواجهة بقوائم القيم لكل معيار.
///
/// **لماذا؟** رصدنا بكيان أن قوائم التعريفات ليست زينة بل **وقود** بواني الشروط
/// والنماذج (24 مصدر قائمة). وعملياً: معيار يُملأ بالكتابة الحرّة يعطي شرطاً
/// صامتاً لا يطابق أحداً («ذكر» مقابل «ذكر ») ولا يكتشفه المستخدم إلا بعد شهر.
/// فكل معيار له مصدرٌ يُعرض منسدلةً، وما لا مصدر له يبقى إدخالاً حرّاً بوعي.
///
/// **الاستثناء المقصود: «الموظف» و«المدير المباشر» بلا قائمة** — 1356 صفّاً
/// بمنسدلة تشلّ الصفحة، والاستعمال الصحيح لهما هو اللصق بـ«ضمن» (نفس منطق
/// <see cref="PayrollRunScope"/>) لا الاختيار واحداً واحداً.
/// </summary>
public static class HrConditionOptions
{
    public sealed record Option(string Value, string Label);

    /// <summary>معايير تُملأ من أعمدة الموظفين نفسها (قيمها نصوص حرّة تاريخياً).</summary>
    private static readonly (string Criterion, string Column)[] DistinctColumns =
    {
        ("gender", "Gender"),
        ("maritalstatus", "MaritalStatus"),
        ("employmentstatus", "EmploymentStatus"),
        ("country", "Country"),
        ("religion", "Religion"),
        ("nationality", "Nationality"),
        ("contracttype", "ContractType"),
        ("worktype", "WorkType"),
        ("jobgrade", "JobGrade"),
        ("sponsor", "SponsorName")
    };

    private static readonly (string Criterion, string Table)[] ReferenceTables =
    {
        ("branch", "Branches"),
        ("department", "Departments"),
        ("company", "Companies"),
        ("position", "Positions")
    };

    /// <summary>
    /// يبني JSON الكتالوج الذي يقرؤه <c>zynora-conditions.js</c>.
    /// أي عطل بمصدر قائمة (جدول غير موجود ببيئة) يسقط القائمةَ وحدها فيبقى المعيار
    /// إدخالاً حرّاً — لا تُسقط الصفحة كلّها لأجل منسدلة.
    /// </summary>
    public static async Task<string> BuildCatalogJsonAsync(ApplicationDbContext db)
    {
        var customFields = await SafeAsync(
            () => HrConditionFacts.LoadCustomFieldDefinitionsAsync(db),
            new List<(string, string)>());

        var criteria = HrConditionCatalog.WithCustomFields(customFields);
        var options = new Dictionary<string, List<Option>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (criterion, table) in ReferenceTables)
        {
            var rows = await SafeAsync(() => LoadReferenceAsync(db, table), new List<Option>());
            if (rows.Count > 0) options[criterion] = rows;
        }

        foreach (var (criterion, column) in DistinctColumns)
        {
            var rows = await SafeAsync(() => LoadDistinctAsync(db, column), new List<Option>());
            if (rows.Count > 0) options[criterion] = rows;
        }

        var payload = criteria.Select(criterion => new
        {
            key = criterion.Key,
            label = criterion.Label,
            kind = criterion.Kind.ToString(),
            category = criterion.Category,
            options = options.TryGetValue(criterion.Key, out var list) ? list : null
        });

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static async Task<List<Option>> LoadReferenceAsync(ApplicationDbContext db, string table) =>
        await HrmsDatabase.QueryAsync(
            db,
            // اسم الجدول من مصفوفة ثابتة بهذا الملف لا من إدخال مستخدم — لا حقن.
            $"SELECT Id, Name FROM {table} WHERE ISNULL(IsDeleted, 0) = 0 ORDER BY Name;",
            command => { },
            reader => new Option(
                HrmsDatabase.GetInt(reader, "Id").ToString(),
                HrmsDatabase.GetString(reader, "Name")));

    private static async Task<List<Option>> LoadDistinctAsync(ApplicationDbContext db, string column) =>
        await HrmsDatabase.QueryAsync(
            db,
            $"""
SELECT DISTINCT LTRIM(RTRIM({column})) AS Value
FROM Employees
WHERE ISNULL(IsDeleted, 0) = 0 AND {column} IS NOT NULL AND LTRIM(RTRIM({column})) <> N''
ORDER BY Value;
""",
            command => { },
            reader =>
            {
                var value = HrmsDatabase.GetString(reader, "Value");
                return new Option(value, value);
            });

    private static async Task<T> SafeAsync<T>(Func<Task<T>> load, T fallback)
    {
        try
        {
            return await load();
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
