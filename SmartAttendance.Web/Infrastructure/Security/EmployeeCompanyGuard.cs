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

    /// <summary>
    /// من مجموعة معرّفات موظفين واردة من الطلب، يُعيد **المسموح منها فقط** ضمن نطاق
    /// شركات المستخدم — الأساس لعمليات التحديد الجماعي (تعيين مناوبة/موقع/مهمة لعدّة
    /// موظفين دفعةً).
    ///
    /// <para><b>لماذا مركزيّة:</b> شاشات «التحديد الجماعي» تستقبل قائمة
    /// <c>SelectedIds</c> من النموذج وتكتب لكلٍّ بلا فحص — نفس الثغرة مكرّرةً. فحصٌ
    /// فرديّ لكل معرّف = استعلامٌ لكل موظف؛ وهذا الحصر يتمّ باستعلامٍ واحد.</para>
    ///
    /// <para><b>مغلق الفشل:</b> نطاق مرفوض ⟹ فارغ؛ ومعرّفات غير صالحة تُسقَط. الأدمن
    /// (غير المقيَّد) يُعيد كل المعرّفات الموجبة كما هي بلا استعلام. المتّصل يقارن حجم
    /// المُعاد بالمطلوب: أي نقصٍ يعني معرّفاً خارج النطاق ⟹ يرفض الدفعة كلها.</para>
    ///
    /// <para><b>لا سطح حقن:</b> المعرّفات أعدادٌ صحيحة تُرشَّح <c>&gt; 0</c> قبل الحقن،
    /// وشرط النطاق يُبنى من أعداد <see cref="CompanyScope"/> المتحقَّق منها — لا مدخل نصّيّ.</para>
    /// </summary>
    public static async Task<HashSet<int>> FilterEmployeesInScopeAsync(
        ApplicationDbContext dbContext,
        IEnumerable<int> employeeIds,
        CompanyScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(scope);

        var ids = (employeeIds ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0 || scope.IsDeniedAll) return new HashSet<int>();
        if (scope.IsUnrestricted) return ids.ToHashSet();

        // المعرّفات أعدادٌ صحيحة مُرشَّحة (> 0) وشرط النطاق من أعداد متحقَّق منها.
        var idList = string.Join(", ", ids);
        var predicate = scope.ToSqlPredicate("e.CompanyId");

        var allowed = await HrmsDatabase.QueryAsync(
            dbContext,
            $"SELECT e.Id FROM Employees e WHERE e.Id IN ({idList}) AND ISNULL(e.IsDeleted, 0) = 0 AND {predicate};",
            configure: null,
            reader => HrmsDatabase.GetInt(reader, "Id"));

        return allowed.ToHashSet();
    }

    /// <summary>
    /// نظير <see cref="FilterEmployeesInScopeAsync"/> لكيانات تملكها صفوفٌ عبر
    /// <c>EmployeeId</c> (تعديل مناوبة · عهدة · مهمة): من مجموعة معرّفات صفوفٍ واردة
    /// من الطلب، يُعيد المسموح منها فقط ضمن نطاق شركاتي — لحذفٍ/تعديلٍ جماعيّ آمن.
    ///
    /// <para>اسما الجدول والعمود ثابتان بالكود ويمرّان بـ<see cref="GuardIdentifier"/>،
    /// والمعرّفات أعداد صحيحة مُرشَّحة — لا سطح حقن. مغلق الفشل كنظيره.</para>
    /// </summary>
    public static async Task<HashSet<int>> FilterOwnedRowsInScopeAsync(
        ApplicationDbContext dbContext,
        string table,
        string idColumn,
        IEnumerable<int> ids,
        CompanyScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(scope);

        var rowIds = (ids ?? Array.Empty<int>())
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (rowIds.Count == 0 || scope.IsDeniedAll) return new HashSet<int>();
        if (scope.IsUnrestricted) return rowIds.ToHashSet();

        GuardIdentifier(table);
        GuardIdentifier(idColumn);

        var idList = string.Join(", ", rowIds);
        var predicate = scope.ToSqlPredicate("e.CompanyId");

        var allowed = await HrmsDatabase.QueryAsync(
            dbContext,
            $"SELECT t.{idColumn} FROM {table} t INNER JOIN Employees e ON e.Id = t.EmployeeId WHERE t.{idColumn} IN ({idList}) AND {predicate};",
            configure: null,
            reader => HrmsDatabase.GetInt(reader, idColumn));

        return allowed.ToHashSet();
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
        public const string MissingPunchRequests = "MissingPunchRequests";
        public const string EmployeeContracts = "EmployeeContracts";
        public const string EmployeeContractMovements = "EmployeeContractMovements";
        public const string EmployeeEndOfService = "EmployeeEndOfService";
        public const string EmployeeSalaryRaises = "EmployeeSalaryRaises";
        public const string AttendanceRecords = "AttendanceRecords";
        public const string ShiftOverrides = "ShiftOverrides";
        public const string EmployeeWebAuthnCredentials = "EmployeeWebAuthnCredentials";
        public const string EmployeeFileRecords = "EmployeeFileRecords";
        public const string EmployeeTasks = "EmployeeTasks";
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
