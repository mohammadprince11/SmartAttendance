using System.Text;
using SmartAttendance.Infrastructure.Persistence;

namespace SmartAttendance.Web.Infrastructure.Hrms;

/// <summary>
/// يفحص هل لموظفٍ **أثرٌ تشغيليّ** — صفٌّ في أيّ جدولٍ تشغيليّ (حضور · رواتب ·
/// عقود · إجازات · قروض · طلبات · مستندات · مخالفات · موافقات · إنهاء خدمة).
///
/// <para>يفصل هذا الحذفَ الإداريّ (لسجلٍّ باطلٍ أُنشئ خطأً، بلا أثر) عن إنهاء
/// الخدمة (لموظفٍ حقيقيٍّ له تاريخ). وجودُ صفٍّ واحدٍ فقط يكفي لرفض الحذف الإداريّ
/// وتوجيه المستخدم لإنهاء الخدمة الذي يحفظ التاريخ بتاريخ/سبب/نوع.</para>
///
/// <para>كل جدولٍ محروسٌ بـ<c>OBJECT_ID</c>/<c>COL_LENGTH</c>، فالمفقود (لم يُنشئه
/// الشفاء الذاتيّ بعد) أو الذي بلا عمود <c>EmployeeId</c> يُتخطّى بلا خطأ. أسماء
/// الجداول ثوابت ترجمة (لا مدخل مستخدم) فلا سطح حقن، والحارس النصّيّ يؤكّد ذلك.</para>
/// </summary>
public static class EmployeeOperationalHistoryGuard
{
    /// <summary>
    /// الجداول التشغيلية المفتاحُها <c>EmployeeId</c>. وجودُ صفٍّ في أيٍّ منها ⟹ للموظف
    /// تاريخٌ تشغيليّ. جداولُ الإعداد/التعريف (أنواع/قوالب/فئات) ليست هنا عمداً — فهي
    /// لا تخصّ موظفاً بعينه. تُضاف الجداول لا تُحذف: إغفالُ جدولٍ يفتح حذفاً لسجلٍّ له أثر.
    /// </summary>
    public static readonly IReadOnlyList<string> OperationalTables = new[]
    {
        // الحضور
        "AttendanceRecords", "DayAttendances", "AttendanceTransactions",
        "EmployeeMonthAttendance", "EmployeeWeekAttendance", "MissingPunchRequests",
        // الرواتب والمالية
        "PayrollRunLines", "PayrollTransactions", "EmployeeSalaryRaises", "EmployeeLoans",
        // العقود
        "EmployeeContracts", "EmployeeContractMovements",
        // الإجازات والطلبات والموافقات
        "LeaveRequests", "SelfServiceRequests", "ApprovalRequestFlows",
        // المستندات
        "EmployeeDocuments", "GeneratedDocuments", "DocumentRequests",
        // المخالفات وإنهاء الخدمة
        "EmployeeViolationCases", "EmployeeEndOfService",
    };

    /// <summary>اسم أوّل جدولٍ تشغيليٍّ فيه صفٌّ لهذا الموظف، أو <c>null</c> إن كان السجلّ نظيفاً.</summary>
    public static async Task<string?> FindFirstNonEmptyAsync(ApplicationDbContext db, int employeeId)
    {
        if (employeeId <= 0) return null;

        var sql = new StringBuilder("DECLARE @hit nvarchar(128) = NULL;\n");
        foreach (var table in OperationalTables)
        {
            GuardIdentifier(table);
            sql.Append(
                $"IF @hit IS NULL AND OBJECT_ID('{table}', 'U') IS NOT NULL AND COL_LENGTH('{table}', 'EmployeeId') IS NOT NULL\n" +
                $"    IF EXISTS (SELECT 1 FROM {table} WHERE EmployeeId = @Id) SET @hit = N'{table}';\n");
        }
        sql.Append("SELECT @hit;");

        return await HrmsDatabase.ScalarAsync<string>(
            db, sql.ToString(),
            command => HrmsDatabase.AddParameter(command, "@Id", employeeId));
    }

    /// <summary>هل للموظف أيّ أثرٍ تشغيليّ؟</summary>
    public static async Task<bool> HasAnyAsync(ApplicationDbContext db, int employeeId) =>
        await FindFirstNonEmptyAsync(db, employeeId) is not null;

    /// <summary>
    /// اسم الجدول يُحقن نصّاً بالاستعلام، فيجب أن يبقى معرّفاً صالحاً (حروف/أرقام/شرطة
    /// سفلية). أيّ محرفٍ آخر يرمي — حارسٌ ضدّ خطأٍ برمجيٍّ لا مدخل مستخدم.
    /// </summary>
    public static void GuardIdentifier(string table)
    {
        if (string.IsNullOrEmpty(table))
            throw new ArgumentException("اسم جدولٍ فارغ.", nameof(table));

        foreach (var ch in table)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_')
                throw new ArgumentException($"اسم جدولٍ غير صالح: {table}", nameof(table));
        }
    }
}
