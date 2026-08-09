using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Issue 5 (P0 IDOR): انتقالات الاعتماد الأسبوعي — نفس ثغرة الشهري — كانت بلا فحص
/// شركة. الحارس النصّي: التواقيع تستقبل <c>CompanyScope</c> والكتابة مربوطةٌ بشركة
/// الموظف عبر join.
/// </summary>
public class WeekAttendanceScopeTests
{
    private static string Store()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "SmartAttendance.Web", "Infrastructure", "Hrms", "WeekAttendanceStore.cs"));
    }

    [Fact]
    public void Transitions_TakeCompanyScope_AndGateByEmployeeCompany()
    {
        var s = Store();
        Assert.Contains("ApproveAsync(ApplicationDbContext dbContext, CompanyScope scope", s);
        Assert.Contains("ReopenAsync(ApplicationDbContext dbContext, CompanyScope scope", s);
        Assert.Contains("LockAsync(ApplicationDbContext dbContext, CompanyScope scope", s);

        var idx = s.IndexOf("private static async Task<int> Transition", System.StringComparison.Ordinal);
        Assert.True(idx > 0);
        var body = s[idx..System.Math.Min(s.Length, idx + 1100)];
        Assert.Contains("INNER JOIN Employees e", body);
        Assert.Contains("ToSqlPredicate", body);
        Assert.Contains("scope.IsDeniedAll", body);
    }
}
