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
        Assert.Contains("ApproveWithGateAsync(\n        ApplicationDbContext dbContext, CompanyScope scope", s.Replace("\r\n", "\n"));
        Assert.Contains("ReopenAsync(ApplicationDbContext dbContext, CompanyScope scope", s);
        Assert.Contains("LockAsync(ApplicationDbContext dbContext, CompanyScope scope", s);
        Assert.Contains("UnlockAsync(\n        ApplicationDbContext dbContext, CompanyScope scope", s.Replace("\r\n", "\n"));

        var idx = s.IndexOf("private static async Task<int> Transition", System.StringComparison.Ordinal);
        Assert.True(idx > 0);
        var body = s[idx..System.Math.Min(s.Length, idx + 1100)];
        Assert.Contains("INNER JOIN Employees e", body);
        Assert.Contains("ToSqlPredicate", body);
        Assert.Contains("scope.IsDeniedAll", body);
    }

    [Fact]
    public void BuildAndApprovalGate_AreScopedBeforeMaterialization()
    {
        var s = Store();
        Assert.Contains("BuildWeekAsync(\n        ApplicationDbContext dbContext, CompanyScope scope", s.Replace("\r\n", "\n"));
        Assert.Contains("INNER JOIN Employees e ON e.Id = d.EmployeeId", s);
        Assert.Contains("EmployeeCompanyGuard.ListFilter(scope, \"e.CompanyId\")", s);
        Assert.Contains("AnalyzedDays", s);
        Assert.Contains("ExpectedDays", s);
    }

    [Fact]
    public void LockedPeriodUnlock_RequiresReasonAndWritesAudit()
    {
        var s = Store();
        Assert.Contains("string.IsNullOrWhiteSpace(reason)", s);
        Assert.Contains("w.Status = N'Locked'", s);
        Assert.Contains("N'EmployeeWeekAttendance'", s);
        Assert.Contains("N'Unlock'", s);
        Assert.Contains("@Reason", s);
    }
}
