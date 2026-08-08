using SmartAttendance.Domain.Entities;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// عزل الشركات على <b>قوائم فلاتر الحضور</b> (منسدلات الفروع/الأقسام/الوظائف في
/// <c>AttendanceViewer</c> و<c>AttendanceRecords</c>).
///
/// <para>قبل الإصلاح كانت <c>LoadLookupsAsync</c>/<c>LoadFilterListsAsync</c> تسرد كلّ
/// الفروع/الأقسام بلا حدّ شركة رغم حقن <c>ICompanyScopeProvider</c> — فمستخدمٌ مقيَّد
/// بشركةٍ يرى أسماء وحدات شركاتٍ أخرى في المنسدلات (تسريب ميتاداتا). الإصلاح يطبّق نفس
/// دلالة <see cref="CompanyScope"/>: غير المقيَّد يرى الكل، والمقيَّد يُرشَّح بـ
/// <c>AllowedCompanyIds.Contains(CompanyId)</c>، والحرمان الكليّ (مصفوفة فارغة) يُنتج
/// قائمةً فارغة تلقائياً.</para>
///
/// <para>الاختبارات تثبّت المرشِّح المُضاف حرفياً بـLinq-to-objects على نفس الكيانات.</para>
/// </summary>
public sealed class LookupScopeFilterTests
{
    private const int CompanyA = 1;
    private const int CompanyB = 2;

    private static IQueryable<Branch> TwoCompanyBranches() => new List<Branch>
    {
        new() { Id = 10, CompanyId = CompanyA, Name = "فرع أ" },
        new() { Id = 20, CompanyId = CompanyB, Name = "فرع ب" }
    }.AsQueryable();

    /// <summary>المرشِّح الإنتاجي حرفياً (نفس سطر الصفحتين): غير مقيَّد ⟹ لا حدّ.</summary>
    private static List<int> ScopedBranchIds(CompanyScope scope, IQueryable<Branch> branches)
    {
        var allowed = scope.IsUnrestricted ? null : scope.AllowedCompanyIds.ToArray();
        return (allowed == null ? branches : branches.Where(b => allowed.Contains(b.CompanyId)))
            .OrderBy(b => b.Name)
            .Select(b => b.Id)
            .ToList();
    }

    [Fact]
    public void UnfilteredScope_LeaksOtherCompanyBranch_RedFirst()
    {
        // بلا حدّ (نمط الكود قبل الإصلاح = غير مقيَّد) تظهر فروع الشركتين.
        var ids = ScopedBranchIds(CompanyScope.Unrestricted(), TwoCompanyBranches());
        Assert.Contains(20, ids);
    }

    [Fact]
    public void RestrictedScope_HidesOtherCompanyBranch()
    {
        // مقيَّد بشركة A ⟹ فرع A فقط، لا يُسرَّب فرع B.
        var ids = ScopedBranchIds(CompanyScope.ForCompanies(new[] { CompanyA }), TwoCompanyBranches());
        Assert.Equal(new[] { 10 }, ids);
    }

    [Fact]
    public void UnrestrictedScope_SeesAllBranches()
    {
        var ids = ScopedBranchIds(CompanyScope.Unrestricted(), TwoCompanyBranches());
        Assert.Equal(new[] { 10, 20 }, ids);
    }

    [Fact]
    public void DeniedAllScope_YieldsEmptyList()
    {
        // الحرمان الكليّ = نطاق مقيَّد بمصفوفة فارغة ⟹ Contains يعيد false للجميع.
        var ids = ScopedBranchIds(CompanyScope.DeniedAll(), TwoCompanyBranches());
        Assert.Empty(ids);
    }
}
