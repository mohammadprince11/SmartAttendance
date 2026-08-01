using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قراءة/حفظ عضوية أوعية الاحتساب من جدول <c>SalaryBaseMembers</c>.
///
/// الوعاء خاصية **لكل ملف** ضريبة/ضمان: يحرّره المستخدم من داخل نموذج إنشاء
/// أو تعديل الملف. الجدول والبذرة والعمود <c>ProfileId</c> تُنشأ بهجرات محكومة
/// (<see cref="SqlSchemaMigrator"/> — <c>…-07</c> و<c>…-08</c>) لا بالشفاء الذاتي.
///
/// ⚠️ الرجوع عند غياب صفوف الملف هو **افتراضات الكود مباشرة** لا صفٌّ بالقاعدة.
/// جُرِّبت طبقة «افتراض عام» (<c>ProfileId = 0</c>) فتبيّن أنها حالة مخفية قابلة
/// للتلف: عبثٌ بها يغيّر اقتطاع كل ملف دفعةً وبلا أثر ظاهر. مصدر الحقيقة
/// لغير المهيَّأ واحدٌ الآن، والهجرة <c>…-09</c> تحذف الصفوف المبذورة السابقة.
/// ملفٌّ لم تُحدَّد عضويته يجب أن يُنتج أرقام المسير القائمة لا صفراً — اقتطاعٌ
/// يصير صفراً بصمت أسوأ من رسالة خطأ.
/// </summary>
public static class SalaryBaseStore
{
    /// <summary>عضوية وعاء ملف واحد؛ فارغة ⟹ افتراضات الكود.</summary>
    public static async Task<List<string>> MembersAsync(
        ApplicationDbContext dbContext, string baseKey, int profileId)
    {
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT ComponentKey FROM SalaryBaseMembers WHERE BaseKey=@Base AND ProfileId=@Profile ORDER BY SortOrder, Id;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Base", baseKey);
                HrmsDatabase.AddParameter(command, "@Profile", profileId);
            },
            reader => HrmsDatabase.GetString(reader, "ComponentKey"));

        return rows.Count > 0 ? rows : DefaultsFor(baseKey);
    }

    /// <summary>عضوية كل الملفات دفعةً — لتغذية نماذج الصفحة بلا نداء لكل ملف.</summary>
    public static async Task<Dictionary<string, List<string>>> AllAsync(ApplicationDbContext dbContext)
    {
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT BaseKey, ProfileId, ComponentKey FROM SalaryBaseMembers ORDER BY SortOrder, Id;",
            command => { },
            reader => (
                Base: HrmsDatabase.GetString(reader, "BaseKey"),
                Profile: HrmsDatabase.GetInt(reader, "ProfileId"),
                Component: HrmsDatabase.GetString(reader, "ComponentKey")));

        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = CacheKey(row.Base, row.Profile);
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<string>();
            list.Add(row.Component);
        }

        return map;
    }

    /// <summary>مفتاح القاموس الذي تعيده <see cref="AllAsync"/>.</summary>
    public static string CacheKey(string baseKey, int profileId) => $"{baseKey}:{profileId}";

    /// <summary>عضوية ملف من قاموس <see cref="AllAsync"/> بلا نداء قاعدة.</summary>
    public static List<string> Resolve(Dictionary<string, List<string>> map, string baseKey, int profileId)
    {
        if (map.TryGetValue(CacheKey(baseKey, profileId), out var own) && own.Count > 0) return own;
        return DefaultsFor(baseKey);
    }

    /// <summary>يستبدل عضوية وعاء ملف كاملةً بترتيب الإفلات المعطى.</summary>
    public static async Task SaveMembersAsync(
        ApplicationDbContext dbContext, string baseKey, int profileId, IEnumerable<string> components)
    {
        var valid = SalaryBaseComposer.Components.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        var ordered = components
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Where(valid.Contains)
            .Distinct(StringComparer.Ordinal)   // القيد UNIQUE يرفضه، والتركيب يجمعه مرّتين
            .ToList();

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM SalaryBaseMembers WHERE BaseKey=@Base AND ProfileId=@Profile;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Base", baseKey);
                HrmsDatabase.AddParameter(command, "@Profile", profileId);
            });

        for (var i = 0; i < ordered.Count; i++)
        {
            var component = ordered[i];
            var sort = i + 1;

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "INSERT INTO SalaryBaseMembers (BaseKey, ProfileId, ComponentKey, SortOrder) VALUES (@Base, @Profile, @Component, @Sort);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Base", baseKey);
                    HrmsDatabase.AddParameter(command, "@Profile", profileId);
                    HrmsDatabase.AddParameter(command, "@Component", component);
                    HrmsDatabase.AddParameter(command, "@Sort", sort);
                });
        }
    }

    private static List<string> DefaultsFor(string baseKey) =>
        baseKey == SalaryBaseComposer.GosiBaseKey
            ? SalaryBaseComposer.DefaultGosiMembers.ToList()
            : SalaryBaseComposer.DefaultTaxMembers.ToList();
}
