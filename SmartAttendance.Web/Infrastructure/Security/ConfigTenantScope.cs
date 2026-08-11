using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// عزل جداول <b>التهيئة</b> بالشركة (8D–8M): أنواع المناوبات · قواعد المناوبات ·
/// قواعد الفترات · الاستطلاعات. قرار المالك 2026-08-11: per-tenant.
///
/// <para><b>الدلالة:</b> <c>CompanyId IS NULL</c> = تهيئة مشتركة تبقى مرئية للجميع
/// (السلوك القديم حرفيّاً)، والصفوف الجديدة تُنسب لشركة منشئها فتُعزَل. لولا ذلك
/// لاختفت التهيئة القائمة — التي سبقت وجود العمود — عن كل دورٍ مقيَّد دفعةً واحدة.</para>
///
/// <para>⚠️ <b>يُطبَّق على مسارات العرض والتحرير فقط.</b> مسارات محرّك الحضور/الرواتب
/// تحتسب **لكل موظف** لا لمستخدمٍ ينظر، وحصرُها بنطاق المتصفّح يُسقط بيانات من
/// الاحتساب ويُفسد المسير.</para>
///
/// <para>أسماء الجداول ثوابت داخلية بالكود (لا مدخل مستخدم) والمُسنِد يُبنى من أعداد
/// صحيحة مُتحقَّق منها بـ<see cref="CompanyScope"/> — فلا سطح حقن.</para>
/// </summary>
public static class ConfigTenantScope
{
    public const string ShiftTypes = "ShiftTypes";
    public const string ShiftRules = "ShiftRules";
    public const string PeriodRules = "PeriodRules";
    public const string EmployeePolls = "EmployeePolls";

    /// <summary>معرّفات الصفوف التي يراها هذا النطاق (المشترك + ما يخصّ شركاته).</summary>
    public static async Task<HashSet<int>> AllowedIdsAsync(
        ApplicationDbContext dbContext,
        string table,
        CompanyScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return new HashSet<int>(await HrmsDatabase.QueryAsync(
            dbContext,
            $"SELECT Id FROM {table} WHERE {scope.ToSharedConfigSqlPredicate("CompanyId")};",
            command => { },
            reader => reader.GetInt32(0)));
    }

    /// <summary>
    /// حارس ملكية مغلق الفشل: المعرّف المجهول أو التابع لشركة أخرى يعيد <c>false</c>
    /// فيردّ المستدعي <c>NotFound</c> — لا <c>Forbid</c>، كي لا تُكشف وجوديّة الصفّ.
    /// </summary>
    public static async Task<bool> IsInScopeAsync(
        ApplicationDbContext dbContext,
        string table,
        int id,
        CompanyScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (id <= 0)
        {
            return false;
        }

        var found = await HrmsDatabase.ScalarAsync<int?>(
            dbContext,
            $"SELECT TOP 1 1 FROM {table} WHERE Id = @Id AND {scope.ToSharedConfigSqlPredicate("CompanyId")};",
            command => HrmsDatabase.AddParameter(command, "@Id", id));

        return found is not null;
    }

    /// <summary>
    /// ينسب صفّاً جديداً لشركة منشئه. الشرط <c>CompanyId IS NULL</c> يمنع إعادة نسبة
    /// صفٍّ منسوبٍ أصلاً (سرقة تهيئة شركة أخرى بمعرّفٍ مُمرَّر).
    /// </summary>
    public static async Task AssignCompanyAsync(
        ApplicationDbContext dbContext,
        string table,
        int id,
        int? companyId)
    {
        if (companyId is null || id <= 0)
        {
            return;
        }

        await HrmsDatabase.ExecuteAsync(
            dbContext,
            $"UPDATE {table} SET CompanyId = @Company WHERE Id = @Id AND CompanyId IS NULL;",
            command =>
            {
                HrmsDatabase.AddParameter(command, "@Company", companyId.Value);
                HrmsDatabase.AddParameter(command, "@Id", id);
            });
    }

    /// <summary>
    /// شركة النسبة للصفوف الجديدة: تُنسب فقط حين يخصّ المستخدم شركةً واحدة بعينها.
    /// غير المقيَّد (أدمن) يُنشئ تهيئةً مشتركة كالسابق — <c>null</c>.
    /// </summary>
    public static int? OwningCompany(CompanyScope scope) =>
        scope is { IsUnrestricted: false } && scope.AllowedCompanyIds.Count == 1
            ? scope.AllowedCompanyIds.Single()
            : null;
}
