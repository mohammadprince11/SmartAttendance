using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// قراءة/حفظ عضوية أوعية الاحتساب من جدول <c>SalaryBaseMembers</c>.
///
/// الجدول والبذرة يُنشآن بهجرة محكومة (<see cref="SqlSchemaMigrator"/> —
/// <c>20260731-07-salary-base-members</c>) لا بالشفاء الذاتي.
///
/// ⚠️ الرجوع للافتراضي عند غياب صفوف الوعاء **مقصود**: قاعدة لم تُهاجَر بعد،
/// أو وعاء أفرغه المستخدم بالخطأ، يجب أن يُنتجا أرقام المسير القائمة لا صفراً.
/// اقتطاعٌ يصير صفراً بصمت أسوأ بكثير من رسالة خطأ.
/// </summary>
public static class SalaryBaseStore
{
    /// <summary>عضوية وعاء واحد مرتّبة. فارغة ⟹ يرجع الافتراضي المناسب.</summary>
    public static async Task<List<string>> MembersAsync(ApplicationDbContext dbContext, string baseKey)
    {
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT ComponentKey FROM SalaryBaseMembers WHERE BaseKey=@Base ORDER BY SortOrder, Id;",
            command => HrmsDatabase.AddParameter(command, "@Base", baseKey),
            reader => HrmsDatabase.GetString(reader, "ComponentKey"));

        return rows.Count > 0 ? rows : DefaultsFor(baseKey);
    }

    /// <summary>عضوية الوعاءين معاً — نداء واحد لكل تشغيل مسير بدل نداءين.</summary>
    public static async Task<(List<string> Tax, List<string> Gosi)> BothAsync(ApplicationDbContext dbContext)
    {
        var rows = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT BaseKey, ComponentKey FROM SalaryBaseMembers ORDER BY SortOrder, Id;",
            command => { },
            reader => (
                Base: HrmsDatabase.GetString(reader, "BaseKey"),
                Component: HrmsDatabase.GetString(reader, "ComponentKey")));

        var tax = rows.Where(r => r.Base == SalaryBaseComposer.TaxBaseKey).Select(r => r.Component).ToList();
        var gosi = rows.Where(r => r.Base == SalaryBaseComposer.GosiBaseKey).Select(r => r.Component).ToList();

        return (
            tax.Count > 0 ? tax : DefaultsFor(SalaryBaseComposer.TaxBaseKey),
            gosi.Count > 0 ? gosi : DefaultsFor(SalaryBaseComposer.GosiBaseKey));
    }

    /// <summary>يستبدل عضوية وعاء كاملةً بالترتيب المعطى (مفاتيح مجهولة تُهمَل).</summary>
    public static async Task SaveMembersAsync(
        ApplicationDbContext dbContext, string baseKey, IEnumerable<string> components)
    {
        var valid = SalaryBaseComposer.Components.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        var ordered = components
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Where(valid.Contains)
            .Distinct(StringComparer.Ordinal)   // القيد UNIQUE يرفض التكرار، والتركيب يجمعه مرّتين
            .ToList();

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            "DELETE FROM SalaryBaseMembers WHERE BaseKey=@Base;",
            command => HrmsDatabase.AddParameter(command, "@Base", baseKey));

        for (var i = 0; i < ordered.Count; i++)
        {
            var component = ordered[i];
            var sort = i + 1;

            await HrmsDatabase.ExecuteAsync(
                dbContext,
                "INSERT INTO SalaryBaseMembers (BaseKey, ComponentKey, SortOrder) VALUES (@Base, @Component, @Sort);",
                command =>
                {
                    HrmsDatabase.AddParameter(command, "@Base", baseKey);
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
