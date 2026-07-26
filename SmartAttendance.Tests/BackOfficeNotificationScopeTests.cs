using System;
using System.Linq;
using SmartAttendance.Web.Infrastructure.Notifications;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات نقية لنطاق رؤية جرس الـback-office: تحويل الدور/الاسم إلى شرط SQL دون قاعدة
/// بيانات. تضمن أن الأدمن يرى كل ما ليس للموظفين، وأدوار HR ترى الرمز المختصر + اسمها،
/// والموظف لا يرى شيئاً، وأن شرط الفلترة يبني معطياته بأمان (لا حقن).
/// </summary>
public class BackOfficeNotificationScopeTests
{
    // ─────────────── Resolve ───────────────

    [Fact]
    public void Resolve_Admin_SeesAllNonEmployee_NoRoleTokens()
    {
        var scope = BackOfficeNotificationScope.Resolve("Admin", "root");
        Assert.True(scope.SeesAllNonEmployee);
        Assert.Empty(scope.RoleTokens);
        Assert.Equal("root", scope.Username);
        Assert.False(scope.IsEmpty);
    }

    [Fact]
    public void Resolve_Admin_IsCaseInsensitive()
    {
        Assert.True(BackOfficeNotificationScope.Resolve("admin", "x").SeesAllNonEmployee);
    }

    [Fact]
    public void Resolve_HrManager_MatchesExactRole_AndLegacyHrToken()
    {
        var scope = BackOfficeNotificationScope.Resolve("HR Manager", "hr1");
        Assert.False(scope.SeesAllNonEmployee);
        Assert.Contains("HR Manager", scope.RoleTokens);
        Assert.Contains("HR", scope.RoleTokens);
    }

    [Fact]
    public void Resolve_HrOfficer_AlsoGetsLegacyHrToken()
    {
        var scope = BackOfficeNotificationScope.Resolve("HR Officer", "hr2");
        Assert.Contains("HR Officer", scope.RoleTokens);
        Assert.Contains("HR", scope.RoleTokens);
    }

    [Fact]
    public void Resolve_BareHrRole_DoesNotDuplicateToken()
    {
        var scope = BackOfficeNotificationScope.Resolve("HR", "hr3");
        Assert.Single(scope.RoleTokens);
        Assert.Equal("HR", scope.RoleTokens[0]);
    }

    [Fact]
    public void Resolve_NonHrRole_MatchesExactRoleOnly()
    {
        var scope = BackOfficeNotificationScope.Resolve("Finance Viewer", "fin");
        Assert.Equal(new[] { "Finance Viewer" }, scope.RoleTokens.ToArray());
    }

    [Fact]
    public void Resolve_Employee_IsEmptyScope()
    {
        var scope = BackOfficeNotificationScope.Resolve("Employee", "emp");
        Assert.True(scope.IsEmpty);
        Assert.False(scope.SeesAllNonEmployee);
        Assert.Empty(scope.RoleTokens);
    }

    [Fact]
    public void Resolve_EmptyRole_NoUsername_IsEmptyScope()
    {
        Assert.True(BackOfficeNotificationScope.Resolve("", "").IsEmpty);
        Assert.True(BackOfficeNotificationScope.Resolve(null, null).IsEmpty);
    }

    [Fact]
    public void Resolve_EmptyRole_WithUsername_MatchesTargetUserOnly()
    {
        var scope = BackOfficeNotificationScope.Resolve("", "someone");
        Assert.False(scope.IsEmpty);
        Assert.Empty(scope.RoleTokens);
        Assert.Equal("someone", scope.Username);
    }

    [Fact]
    public void Resolve_TrimsWhitespace()
    {
        var scope = BackOfficeNotificationScope.Resolve("  HR Manager  ", "  u  ");
        Assert.Contains("HR Manager", scope.RoleTokens);
        Assert.Equal("u", scope.Username);
    }

    // ─────────────── BuildFilter ───────────────

    [Fact]
    public void BuildFilter_Admin_ExcludesEmployeeRows_AndMatchesUser()
    {
        var scope = BackOfficeNotificationScope.Resolve("Admin", "root");
        var (where, ps) = BackOfficeNotificationScope.BuildFilter(scope);

        Assert.Contains("<> N'Employee'", where);
        Assert.Contains("TargetUser = @scopeUser", where);
        Assert.Contains(ps, p => p.Name == "@scopeUser" && (string)p.Value == "root");
    }

    [Fact]
    public void BuildFilter_HrManager_EmitsOneParamPerToken_PlusUser()
    {
        var scope = BackOfficeNotificationScope.Resolve("HR Manager", "hr1");
        var (where, ps) = BackOfficeNotificationScope.BuildFilter(scope);

        // رمزان (HR Manager + HR) + اسم مستخدم = 3 معطيات، وكلها تظهر في الشرط.
        Assert.Equal(3, ps.Count);
        Assert.Contains("TargetRole = @role0", where);
        Assert.Contains("TargetRole = @role1", where);
        Assert.Contains("@role0", ps.Select(p => p.Name));
        Assert.Contains("@role1", ps.Select(p => p.Name));
        Assert.DoesNotContain("<> N'Employee'", where); // ليس أدمن
    }

    [Fact]
    public void BuildFilter_EmptyScope_MatchesNothing()
    {
        var (where, ps) = BackOfficeNotificationScope.BuildFilter(BackOfficeNotificationScope.Empty);
        Assert.Equal("1 = 0", where);
        Assert.Empty(ps);
    }

    [Fact]
    public void BuildFilter_ParametersAreValues_NotInlinedRoleNames()
    {
        // اسم الدور يُمرَّر كمعطى لا يُدرَج بالنصّ — حماية من الحقن.
        var scope = BackOfficeNotificationScope.Resolve("Finance Viewer", "fin");
        var (where, ps) = BackOfficeNotificationScope.BuildFilter(scope);

        Assert.DoesNotContain("Finance Viewer", where);
        Assert.Contains(ps, p => (string)p.Value == "Finance Viewer");
    }

    [Fact]
    public void BuildFilter_ClausesJoinedWithOr()
    {
        var scope = BackOfficeNotificationScope.Resolve("HR Manager", "hr1");
        var (where, _) = BackOfficeNotificationScope.BuildFilter(scope);
        Assert.Contains(" OR ", where);
    }
}
