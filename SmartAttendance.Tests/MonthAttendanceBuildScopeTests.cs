using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Phase 5 (P0 عزل الشركات): <see cref="MonthAttendanceStore.BuildMonthAsync"/> كان
/// يجمّع يوميات <b>كل الشركات</b> ويُدمجها في <c>EmployeeMonthAttendance</c> بلا أي
/// قيد شركة — والأخطر أن <c>PayrollRunStore.CalculateAsync</c> يستدعيه تلقائياً. أي
/// أن حساب راتب شركة A كان يعيد بناء الحضور الشهري لموظفي B وC أيضاً.
///
/// <para>الإصلاح: <c>CompanyScope</c> إلزامي يقيّد مصدر التجميع (والعدّ المُرجَع)
/// بموظفي النطاق <em>قبل</em> الـMERGE، ومغلق الفشل عند <c>DeniedAll</c>.</para>
///
/// <para>هذه الاختبارات تحرس <b>التوقيع والشرط المولَّد</b> — الجزء النقيّ القابل
/// للاختبار بلا SQL Server. تكامل A/B (قبل/بعد على شركتين) يبقى
/// <b>DB-GATED</b> على قاعدة اختبار غير إنتاجية، لا هنا. لا ندّعي أكثر مما نختبر.</para>
/// </summary>
public class MonthAttendanceBuildScopeTests
{
    private static string Source()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "SmartAttendance.Web", "Infrastructure", "Hrms", "MonthAttendanceStore.cs"));
    }

    /// <summary>🔴 الانحدار الأساسي: التوقيع يجب أن يفرض <c>CompanyScope</c>؛ إسقاطه يعيد الثغرة.</summary>
    [Fact]
    public void BuildMonthAsync_يفرض_CompanyScope()
    {
        var method = typeof(MonthAttendanceStore).GetMethod(
            nameof(MonthAttendanceStore.BuildMonthAsync), BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Contains(method!.GetParameters(), p => p.ParameterType == typeof(CompanyScope));
    }

    /// <summary>مصدر التجميع (CTE) والعدّ المُرجَع يُقيَّدان بشركة الموظف قبل الدمج.</summary>
    [Fact]
    public void BuildMonthAsync_يقيّد_التجميع_والعدّ_بالنطاق()
    {
        var source = Source();
        var idx = source.IndexOf("BuildMonthAsync", System.StringComparison.Ordinal);
        Assert.True(idx > 0);
        // جسم الدالة حتى نهاية العدّ المُرجَع.
        var end = source.IndexOf("public static async Task<List<MonthRow>> ListAsync", idx, System.StringComparison.Ordinal);
        Assert.True(end > idx);
        var body = source[idx..end];

        // التجميع يمرّ عبر Employees، والشرط المولَّد يحصر الشركة في موضعين
        // (CTE + عدّ الإرجاع) — لا استعلام خام بلا قيد شركة.
        Assert.Contains("INNER JOIN Employees e ON e.Id = d.EmployeeId", body);
        Assert.Contains("INNER JOIN Employees e ON e.Id = m.EmployeeId", body);
        Assert.Equal(2, Regex.Matches(body, @"EmployeeCompanyGuard\.ListFilter\(scope, ""[em]\.CompanyId""\)").Count);
        // مغلق الفشل: DeniedAll يرجع 0 قبل لمس القاعدة.
        Assert.Contains("scope.IsDeniedAll", body);
    }

    /// <summary>مغلق الفشل على مستوى الشرط: لا شركة مسموحة ⟹ <c>1 = 0</c> لا شرط مُهمَل.</summary>
    [Fact]
    public void النطاق_المرفوض_كليّاً_يغلق_الفشل()
    {
        var scope = CompanyScope.DeniedAll();
        Assert.True(scope.IsDeniedAll);
        Assert.Equal("1 = 0", scope.ToSqlPredicate("e.CompanyId"));
    }

    /// <summary>النطاق المقيَّد يحصر التجميع بشركاته وحدها.</summary>
    [Fact]
    public void النطاق_المقيّد_يحصر_الشركة()
    {
        Assert.Equal("e.CompanyId IN (2)", CompanyScope.ForCompanies(new[] { 2 }).ToSqlPredicate("e.CompanyId"));
    }
}
