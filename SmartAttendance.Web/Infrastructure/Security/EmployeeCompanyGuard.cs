using SmartAttendance.Infrastructure.Persistence;
using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Web.Infrastructure.Security;

/// <summary>
/// «هل هذا الموظف ضمن شركاتي؟» — الفحص الذي كانت أربع شاشات تفتقده.
///
/// <para><b>لماذا يلزم رغم وجود الحارس المركزيّ:</b> الحارس يفحص ملكية الكيان
/// لمسارات <c>/employees/*</c> وحدها؛ كل ما عداها يُفحص بمستوى المسار والدور فقط —
/// «هل تفتح هذه الشاشة؟» لا «هل هذا الصفّ لك؟». وهذا الفرق هو جذر كل ثغرة رُصدت
/// بمسح العزل (MULTI-TENANT-ISOLATION-SCAN.md).</para>
///
/// <para><b>لماذا شركة لا نطاق كامل:</b> الفجوة المُثبَتة عبورُ الشركات. والفحص
/// الأعمق (فرع/قسم) محلّه الحارس المركزيّ حيث يُفحص مع رمز صلاحية مناسب لكل مسار —
/// إقحامه هنا يفرض على كل شاشة اختيار رمزٍ لا يخصّها.</para>
///
/// <para><b>مغلق الفشل:</b> موظف غير موجود، أو بلا شركة، أو خارج النطاق ⟹
/// <c>false</c>. ومعرّف غير صالح كذلك.</para>
/// </summary>
public static class EmployeeCompanyGuard
{
    public static async Task<bool> CanAccessEmployeeAsync(
        ApplicationDbContext dbContext,
        int employeeId,
        CompanyScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(scope);

        if (employeeId <= 0 || scope.IsDeniedAll) return false;
        if (scope.IsUnrestricted) return true;

        var companyId = await HrmsDatabase.QueryAsync(
            dbContext,
            "SELECT CompanyId FROM Employees WHERE Id = @Id AND ISNULL(IsDeleted, 0) = 0;",
            command => HrmsDatabase.AddParameter(command, "@Id", employeeId),
            reader => HrmsDatabase.GetNullableInt(reader, "CompanyId"));

        // لا صفّ ⟹ رفض. ولا نميّز «غير موجود» عن «ممنوع» فلا يُستدلّ على وجود
        // موظفي شركةٍ أخرى بفرق الاستجابات.
        return companyId.Count == 1 && scope.Allows(companyId[0]);
    }

    /// <summary>
    /// شرط SQL يحصر سرداً على موظفي شركات المستخدم.
    ///
    /// <para>للشاشات التي تسرد **كثيرين** لا واحداً: هناك لا يوجد «موظف مستهدَف»
    /// يُفحص، بل مجموعةٌ تُرشَّح. الرفض غير وارد — المطلوب ألّا يظهر ما ليس لك.</para>
    /// </summary>
    public static string ListFilter(CompanyScope scope, string employeeCompanyColumn)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.ToSqlPredicate(employeeCompanyColumn);
    }
}
