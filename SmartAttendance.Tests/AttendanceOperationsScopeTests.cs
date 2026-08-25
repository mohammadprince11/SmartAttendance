using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Issue 1 (P0 عزل الشركات): تعديلات الحضور اليدوية بـAttendanceOperations كانت
/// تحسم الموظف من رقمه بلا نطاق شركة، فمستخدم شركة A يعدّل/يحذف حضور موظف شركة B.
/// حارس نصّي: كل حسمٍ يمرّ بـ`ResolveScopedEmployeeIdAsync`، ولا يبقى استعلامُ
/// رقمِ موظفٍ خامٌّ بلا نطاق، والحذف بالمعرّف مربوطٌ بشركة الموظف.
/// </summary>
public class AttendanceOperationsScopeTests
{
    private static string Source()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "SmartAttendance.Web", "Pages", "AttendanceOperations", "Index.cshtml.cs"));
    }

    [Fact]
    public void ManualEdits_ResolveEmployeeThroughScopedHelper()
    {
        var source = Source();
        Assert.Contains("ResolveScopedEmployeeIdAsync", source);
        Assert.Contains("EmployeeCompanyGuard", source);
        // كل معالجات التعديل تستعمل الحاسم المنطوق بالنطاق.
        Assert.Contains("ResolveScopedEmployeeIdAsync(Correction.EmployeeNo)", source);
        Assert.Contains("ResolveScopedEmployeeIdAsync(OtherPunch.EmployeeNo)", source);
    }

    [Fact]
    public void NoRawUnscopedEmployeeNoLookupRemains()
    {
        var source = Source();
        // لا يبقى استعلام «SELECT TOP 1 Id FROM Employees WHERE EmployeeNo» إلا داخل
        // الحاسم المنطوق بالنطاق (مرّة واحدة).
        var matches = Regex.Matches(source, @"SELECT TOP 1 Id FROM Employees WHERE EmployeeNo");
        Assert.Single(matches.Cast<Match>());
    }

    [Fact]
    public void DeleteOtherPunch_IsCompanyScoped()
    {
        var source = Source();
        var idx = source.IndexOf("OnPostDeleteOtherPunchAsync", System.StringComparison.Ordinal);
        Assert.True(idx > 0);
        var body = source[idx..System.Math.Min(source.Length, idx + 900)];
        Assert.Contains("INNER JOIN Employees e", body);
        Assert.Contains("ToSqlPredicate", body);
    }
}
