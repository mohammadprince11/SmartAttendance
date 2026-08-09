using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Issue 4 (P0 IDOR): انتقالات الاعتماد الشهري (اعتماد/إرجاع/قفل) كانت تعدّل
/// <c>WHERE Id IN (SelectedIds)</c> بلا فحص شركة، فمستخدم شركة A ينقل صفوف شركة B
/// بمعرّفات مزوّرة. الحارس النصّي: التواقيع تستقبل <c>CompanyScope</c> والكتابة
/// مربوطةٌ بشركة الموظف عبر join.
/// </summary>
public class MonthAttendanceScopeTests
{
    private static string Store()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "SmartAttendance.Web", "Infrastructure", "Hrms", "MonthAttendanceStore.cs"));
    }

    [Fact]
    public void Transitions_TakeCompanyScope()
    {
        var s = Store();
        Assert.Contains("ApproveWithGateAsync(\n        ApplicationDbContext dbContext, CompanyScope scope", s.Replace("\r\n", "\n"));
        Assert.Contains("ReopenAsync(ApplicationDbContext dbContext, CompanyScope scope", s);
        Assert.Contains("LockAsync(ApplicationDbContext dbContext, CompanyScope scope", s);
    }

    [Fact]
    public void TransitionWrite_IsGatedByEmployeeCompany()
    {
        var s = Store();
        var idx = s.IndexOf("private static async Task<int> Transition", System.StringComparison.Ordinal);
        Assert.True(idx > 0);
        var body = s[idx..System.Math.Min(s.Length, idx + 1200)];
        Assert.Contains("INNER JOIN Employees e", body);
        Assert.Contains("ToSqlPredicate", body);
        Assert.Contains("scope.IsDeniedAll", body);
    }
}
