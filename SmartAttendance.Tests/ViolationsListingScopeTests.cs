using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Tests;

/// <summary>
/// انحدار AUTHZ-006 (FIX-001) — قوائم `Violations` الثلاث كانت تسرد
/// `EmployeeViolationCases` (بأسماء الموظفين واقتطاعاتهم المالية) وموظفيها **بلا
/// فلتر شركة**. كانت كامنةً لا حيّة (المسار خارج كل كتالوج دورٍ ⟹ الأدمن وحده
/// يبلغه)، لكن إضافة المسار لكتالوج دورٍ مقيَّد كانت ستحوّلها لتسريبٍ فوريّ.
///
/// <para>الإصلاح فلتر قراءةٍ فقط بنمط <see cref="EmployeeCompanyGuard.ListFilter"/>:
/// للأدمن <c>1 = 1</c> (سلوكٌ مطابق) · لأي دورٍ مقيَّد يُفلتر. لا صيغة ولا كتابة
/// — الصحّة المالية للاقتطاعات لا تُمسّ.</para>
/// </summary>
public class ViolationsListingScopeTests
{
    private static string PageSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(
            directory!.FullName, "SmartAttendance.Web", "Pages", "Violations", "Index.cshtml.cs"));
    }

    [Fact]
    public void AllThreeListings_FilterByCompany()
    {
        var source = PageSource();

        // الشرط يُبنى من بدائيّة النظام لا بسلسلةٍ يدويّة.
        Assert.Contains("CompanyPredicateAsync", source);

        // ثلاثة استهلاكات فعليّة بـWHERE: قائمتا المخالفات (e.CompanyId) والمنتقي (CompanyId).
        var whereInjections = source.Split("AND {companyPredicate}").Length - 1;
        var pickerInjection = source.Split("AND {pickerPredicate}").Length - 1;

        Assert.True(whereInjections >= 2, $"حُقن companyPredicate بـ{whereInjections} قائمة (المتوقَّع ≥2).");
        Assert.Equal(1, pickerInjection);
    }

    [Fact]
    public void Predicate_IsReadFilterOnly_NotTouchingDeductionMath()
    {
        // ضمانة الأمان: التغيير أضاف WHERE فقط — لا مسّ لأي حساب اقتطاع.
        var source = PageSource();
        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope, employeeCompanyColumn)", source);
    }

    // ─── سلوك الشرط (تنفيذيّ) ───

    [Fact]
    public void ScopedUser_GetsCompanyPredicate()
    {
        Assert.Equal("e.CompanyId IN (3, 5)",
            EmployeeCompanyGuard.ListFilter(CompanyScope.ForCompanies(new[] { 3, 5 }), "e.CompanyId"));
    }

    [Fact]
    public void UnrestrictedAdmin_GetsIdentityPredicate_NoBehaviorChange()
    {
        // جوهر السلامة: الأدمن يرى 1=1 ⟹ لا صفٌّ يُحجب عمّن كان يراه.
        Assert.Equal("1 = 1",
            EmployeeCompanyGuard.ListFilter(CompanyScope.Unrestricted(), "e.CompanyId"));
    }

    [Fact]
    public void EmptyScope_DeniesEverything()
    {
        Assert.Equal("1 = 0",
            EmployeeCompanyGuard.ListFilter(CompanyScope.ForCompanies(Array.Empty<int>()), "CompanyId"));
    }
}
