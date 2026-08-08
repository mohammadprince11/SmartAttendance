using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// عزل تحليل الحضور — الإصلاح العاجل لأخطر ثغرة أثبتتها الدراسة
/// (`docs/ZYNORA-TENANT-ISOLATION-STUDY-2026-08-08.md` القسم P0-2):
/// <c>DELETE FROM DayAttendances WHERE WorkDate BETWEEN @From AND @To</c> بلا أي
/// قيد شركة ⟹ مستخدم الشركة (1) يضغط «تحديث الحضور» فيُتلف حضور 1,728 موظفاً
/// بالشركتين (2) و(3).
///
/// <para>هذه الاختبارات تحرس <b>الشرط المولَّد</b> — الجزء النقيّ القابل للاختبار
/// بلا SQL Server. تكامل التحليل نفسه يُتحقَّق منه بمقارنة قبل/بعد على قاعدة
/// <c>SmartAttendance_Dev</c> (موثّقة بالتسليم)، لا هنا. لا ندّعي أكثر مما نختبر.</para>
/// </summary>
public class AttendanceAnalysisCompanyScopeTests
{
    /// <summary>
    /// 🔴 الانحدار الأساسي: نطاقٌ مقيَّد **يجب** أن يولّد شرطاً يحصر الحذف.
    /// شرطٌ فارغ أو <c>1=1</c> هنا يعني عودة الثغرة.
    /// </summary>
    [Fact]
    public void النطاق_المقيّد_يولّد_شرط_حصر()
    {
        var predicate = CompanyScope.ForCompanies(new[] { 2 }).ToSqlPredicate("e.CompanyId");

        Assert.Equal("e.CompanyId IN (2)", predicate);
        Assert.DoesNotContain("1 = 1", predicate);
    }

    /// <summary>مستخدم بعدّة شركات يرى شركاته وحدها — مرتّبةً ليكون النصّ حتمياً.</summary>
    [Fact]
    public void عدّة_شركات_تُدرَج_مرتّبة()
    {
        Assert.Equal(
            "e.CompanyId IN (1, 2, 3)",
            CompanyScope.ForCompanies(new[] { 3, 1, 2 }).ToSqlPredicate("e.CompanyId"));
    }

    /// <summary>
    /// مغلق الفشل: لا شركة مسموحة ⟹ <c>1 = 0</c> لا شرط مُهمَل. الفرق بينهما هو
    /// الفرق بين «لا تحذف شيئاً» و«احذف كل شيء».
    /// </summary>
    [Fact]
    public void المحروم_يولّد_شرطاً_مانعاً_لا_مُهمَلاً()
    {
        Assert.Equal("1 = 0", CompanyScope.DeniedAll().ToSqlPredicate("e.CompanyId"));
    }

    /// <summary>
    /// غير المقيَّد (الأدمن) يعيد <c>1 = 1</c> — والمتجر يستعمل عندها **الأمر
    /// الأصلي حرفياً** لا هذه الصيغة، فيبقى سلوكه غير متغيّر بمقدار صفّ واحد.
    /// </summary>
    [Fact]
    public void غير_المقيّد_لا_يغيّر_شيئاً()
    {
        Assert.True(CompanyScope.Unrestricted().IsUnrestricted);
        Assert.Equal("1 = 1", CompanyScope.Unrestricted().ToSqlPredicate("e.CompanyId"));
    }

    /// <summary>
    /// النطاق المقيَّد لا يسمح بصفٍّ بلا شركة — نفس قرار
    /// <see cref="CompanyScope.Allows"/>: يراها الأدمن وحده حتى تُنسَب. وهذا يطابق
    /// شرط EF المُطبَّق على مجموعة الموظفين (<c>CompanyId != null &amp;&amp; IN</c>).
    /// </summary>
    [Fact]
    public void الصفّ_بلا_شركة_لا_يدخل_النطاق_المقيّد()
    {
        var scope = CompanyScope.ForCompanies(new[] { 1 });

        Assert.False(scope.Allows(null));
        Assert.False(scope.Allows(0));
        Assert.True(scope.Allows(1));
        Assert.True(CompanyScope.Unrestricted().Allows(null));
    }

    /// <summary>
    /// الشرط يُبنى من أعداد صحيحة مُتحقَّق منها بالمُنشئ (<c>id &gt; 0</c>) لا من
    /// مدخل مستخدم — فلا سطح حقن حتى لو مُرِّرت قيم غير صالحة.
    /// </summary>
    [Fact]
    public void القيم_غير_الصالحة_تُسقَط_لا_تُحقَن()
    {
        var scope = CompanyScope.ForCompanies(new[] { 0, -5, 7 });

        Assert.Equal("e.CompanyId IN (7)", scope.ToSqlPredicate("e.CompanyId"));
    }
}
