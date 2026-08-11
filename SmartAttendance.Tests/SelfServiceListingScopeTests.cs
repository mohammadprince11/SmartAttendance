using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Tests;

/// <summary>
/// انحدار AUTHZ-005 — سردُ الخدمة الذاتية كان **بلا أي فلتر**: آخر 100 طلبٍ من كل
/// الشركات بأسماء أصحابها وأسباب طلباتهم، ويبلغه أدنى دور بالنظام (الحارس المركزيّ
/// يمرّر كل <c>GET</c> على <c>/selfservices</c>). الكتابة كانت محروسة
/// (<c>CanAccessEmployeeAsync</c>) — القراءة وحدها كانت مكشوفة.
///
/// <para>نفس عائلة AUTHZ-003 ونفس السبب الجذريّ (ROOT-SEC-01): الإنفاذ بالصفحة لا
/// بالطبقة، فصفحةٌ واحدة تُنسى تكفي.</para>
/// </summary>
public class SelfServiceListingScopeTests
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
            directory!.FullName,
            "SmartAttendance.Web", "Pages", "SelfServices", "Index.cshtml.cs"));
    }

    [Fact]
    public void Listing_FiltersByCompany_NotJustJoinsEmployees()
    {
        var source = PageSource();

        // الشرط يُبنى من نفس بدائيّة النظام لا بسلسلةٍ يدويّة.
        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope, \"e.CompanyId\")", source);

        // والاستعلام يستهلكه فعلاً بـWHERE — لا يبنيه ثم يهمله.
        Assert.Contains("WHERE {companyPredicate}", source);
    }

    [Fact]
    public void EmployeeRole_SeesOnlyOwnRequests()
    {
        var source = PageSource();

        Assert.Contains("ResolveOwnEmployeeIdForEmployeeRole", source);
        Assert.Contains("r.EmployeeId = @OwnEmployeeId", source);
    }

    [Fact]
    public void EmployeeWithoutIdentity_SeesNothing()
    {
        // مغلق الفشل: هويةٌ ناقصة لا تُترجَم إلى سردٍ أوسع.
        var source = PageSource();

        Assert.Contains("DeniedEmployeeId", source);
        Assert.Contains("Requests = new List<RequestRow>();", source);
    }

    // ─── سلوك الشرط نفسه (تنفيذيّ لا نصّيّ) ───

    [Fact]
    public void CompanyPredicate_RestrictsToTheAllowedCompanies()
    {
        var predicate = EmployeeCompanyGuard.ListFilter(
            CompanyScope.ForCompanies(new[] { 7, 9 }), "e.CompanyId");

        Assert.Equal("e.CompanyId IN (7, 9)", predicate);
    }

    [Fact]
    public void CompanyPredicate_DeniesEverything_WhenScopeIsEmpty()
    {
        // الحالة الحاسمة: نطاقٌ فارغ يجب أن يمنع لا أن يسمح.
        var predicate = EmployeeCompanyGuard.ListFilter(
            CompanyScope.ForCompanies(Array.Empty<int>()), "e.CompanyId");

        Assert.Equal("1 = 0", predicate);
    }

    [Fact]
    public void CompanyPredicate_AllowsAll_OnlyForUnrestrictedAdmin()
    {
        Assert.Equal("1 = 1", EmployeeCompanyGuard.ListFilter(
            CompanyScope.Unrestricted(), "e.CompanyId"));
    }
}
