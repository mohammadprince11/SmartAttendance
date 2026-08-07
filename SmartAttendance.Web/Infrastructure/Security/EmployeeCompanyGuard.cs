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
    /// «هل الصفّ <paramref name="id"/> بجدول <paramref name="table"/> يخصّ موظفاً
    /// ضمن شركاتي؟» — لكيانات تخصّ موظفاً بلا أن تكون الموظف نفسه (قرض · طلب ماليّ
    /// · عقد · وثيقة).
    ///
    /// <para>معمَّمة عمداً: عدّة متاجر بنفس الشكل (جدولٌ به <c>EmployeeId</c>)، وكتابة
    /// دوالّ متطابقة لكلٍّ يعني مواضع إضافية لنسيان الفحص.</para>
    ///
    /// <para><b>لا سطح حقن</b>: اسما الجدول والعمود ثابتان بالكود لا مدخلَ مستخدم،
    /// و<see cref="GuardIdentifier"/> يرفض أي محرف خارج المعرّفات الصالحة — فالخطأ
    /// البرمجيّ يُرفع استثناءً لا يُترجم استعلاماً.</para>
    /// </summary>
    public static async Task<bool> CanAccessOwnedRowAsync(
        ApplicationDbContext dbContext,
        string table,
        string idColumn,
        int id,
        CompanyScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(scope);

        if (id <= 0 || scope.IsDeniedAll) return false;
        if (scope.IsUnrestricted) return true;

        GuardIdentifier(table);
        GuardIdentifier(idColumn);

        var companyId = await HrmsDatabase.QueryAsync(
            dbContext,
            $"SELECT e.CompanyId FROM {table} t INNER JOIN Employees e ON e.Id = t.EmployeeId WHERE t.{idColumn} = @Id;",
            command => HrmsDatabase.AddParameter(command, "@Id", id),
            reader => HrmsDatabase.GetNullableInt(reader, "CompanyId"));

        return companyId.Count == 1 && scope.Allows(companyId[0]);
    }

    /// <summary>معرّف SQL صالح فقط — حرفٌ أو شرطة سفلية ثم حروف/أرقام/شرطات سفلية.</summary>
    public static void GuardIdentifier(string identifier)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(identifier ?? string.Empty, "^[A-Za-z_][A-Za-z0-9_]*$"))
        {
            throw new ArgumentException(
                $"معرّف SQL غير صالح: «{identifier}». الأسماء ثابتة بالكود — هذا خطأ برمجيّ لا مدخل مستخدم.",
                nameof(identifier));
        }
    }

    /// <summary>أسماء الجداول المستعملة مع <see cref="CanAccessOwnedRowAsync"/> — ثابتة بالكود.</summary>
    public static class Tables
    {
        public const string EmployeeLoans = "EmployeeLoans";
        public const string SelfServiceRequests = "SelfServiceRequests";
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
