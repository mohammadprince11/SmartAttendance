using System;
using System.IO;
using System.Linq;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// فصل الحذف الإداريّ عن إنهاء الخدمة (People — Task 2).
///
/// <para><b>السياسة</b>: <c>/employees/delete</c> صار **حذفاً إدارياً** لسجلٍّ باطلٍ
/// بلا أثرٍ تشغيليّ — لا «إيقافاً» عامّاً. يتطلّب صلاحيةً مخصّصة
/// (<see cref="PeoplePermissionCodes.AdministrativeDelete"/>) لا يمنحها تفويض الإنهاء
/// العاديّ ولا توافقية الأدوار الحسّاسة، ويُرفض إن وُجد أيّ أثرٍ تشغيليّ (يُوجَّه لإنهاء
/// الخدمة) بلا تحويلٍ تلقائيّ.</para>
///
/// حرّاسٌ نصّيّون/منطقيّون يعملون بكل بناء (بلا قاعدة — القاعدة مشتركة تطوير/إنتاج).
/// </summary>
public class AdministrativeDeletePolicyTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadWeb(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "SmartAttendance.Web" }.Concat(parts).ToArray()));

    // ═══ الصلاحية المخصّصة ═══

    [Fact]
    public void AdministrativeDelete_IsRegisteredInCatalog_AsDistinctCode()
    {
        Assert.Equal("People.AdministrativeDelete", PeoplePermissionCodes.AdministrativeDelete);
        Assert.Contains(PeoplePermissionCodes.AdministrativeDelete, PeoplePermissionCodes.All);
        Assert.Contains(PeoplePermissionCodes.Definitions,
            d => d.Code == PeoplePermissionCodes.AdministrativeDelete);

        // رمزٌ مستقلٌّ لا يساوي الحذف العامّ ولا الإنهاء.
        Assert.NotEqual(PeoplePermissionCodes.Delete, PeoplePermissionCodes.AdministrativeDelete);
        Assert.NotEqual(PeoplePermissionCodes.EndService, PeoplePermissionCodes.AdministrativeDelete);
    }

    /// <summary>
    /// حسّاسٌ كالحذف/الإنهاء: التوافقية تمنحه (Admin · HR Manager) وتمنعه صراحةً
    /// (HR Officer · Branch Manager) — فلا يتسرّب بعضوية بادئة المسار.
    /// </summary>
    [Theory]
    [InlineData("Admin", true)]
    [InlineData("HR Manager", true)]
    [InlineData("HR Officer", false)]
    [InlineData("Branch Manager", false)]
    [InlineData("Employee", false)]
    public void AdministrativeDelete_CompatibilityMatrix(string role, bool expected)
    {
        Assert.Equal(
            expected,
            PeopleCompatibilityAccess.IsAllowed(role, PeoplePermissionCodes.AdministrativeDelete));
    }

    /// <summary>
    /// المسار مسجَّلٌ بالصلاحية المخصّصة لا بـ<c>Delete</c> العامّ — فمن مُنِح «حذفاً»
    /// عاديّاً لا يملك تلقائياً الحذف الإداريّ.
    /// </summary>
    [Fact]
    public void DeleteRoute_RequiresAdministrativeDelete()
    {
        var resolver = ReadWeb("Infrastructure", "Security", "PeopleRoutePermissionResolver.cs");
        var index = resolver.IndexOf("/employees/delete", StringComparison.Ordinal);

        Assert.True(index > 0, "مسار /employees/delete غير مسجَّل بالمحلِّل.");
        var block = resolver[index..Math.Min(resolver.Length, index + 420)];

        Assert.Contains("AdministrativeDelete", block);
        Assert.DoesNotContain("PeoplePermissionCodes.Delete)", block); // لا الحذف العامّ
    }

    // ═══ حارس التاريخ التشغيليّ ═══

    [Fact]
    public void OperationalTables_AreValidIdentifiers_AndCoverEveryDomainInThePolicy()
    {
        Assert.NotEmpty(EmployeeOperationalHistoryGuard.OperationalTables);

        // أسماء صالحة (لا سطح حقن) — لا ترمي.
        foreach (var table in EmployeeOperationalHistoryGuard.OperationalTables)
            EmployeeOperationalHistoryGuard.GuardIdentifier(table);

        // كل مجالٍ ذكرته السياسة ممثَّلٌ بجدولٍ واحدٍ على الأقل.
        string[] required =
        {
            "AttendanceRecords", "DayAttendances",      // حضور
            "PayrollRunLines",                          // رواتب
            "EmployeeContracts",                        // عقود
            "LeaveRequests",                            // إجازات
            "EmployeeLoans",                            // قروض
            "SelfServiceRequests",                      // طلبات مالية
            "EmployeeDocuments",                        // مستندات
            "EmployeeViolationCases",                   // إنذارات/مخالفات
            "EmployeeEndOfService",                     // إنهاء خدمة
        };
        foreach (var table in required)
            Assert.Contains(table, EmployeeOperationalHistoryGuard.OperationalTables);
    }

    [Theory]
    [InlineData("Employees; DROP TABLE x")]
    [InlineData("Employees WHERE 1=1")]
    [InlineData("")]
    public void GuardIdentifier_RejectsAnythingButAPlainIdentifier(string bad)
    {
        Assert.Throws<ArgumentException>(() => EmployeeOperationalHistoryGuard.GuardIdentifier(bad));
    }

    // ═══ الحارس النصّيّ على صفحة الحذف ═══

    [Fact]
    public void DeletePage_EnforcesAdministrativeDeletePermission_InHandler()
    {
        var page = ReadWeb("Pages", "Employees", "Delete.cshtml.cs");

        Assert.Contains("CanAccessEmployeeAsync", page);
        Assert.Contains("PeoplePermissionCodes.AdministrativeDelete", page);
        Assert.Contains("PeopleCompatibilityAccess.IsAllowed", page);
    }

    /// <summary>
    /// POST يستشير حارس التاريخ التشغيليّ ويرفض عند وجوده — فلا يُحذف سجلٌّ له أثر.
    /// </summary>
    [Fact]
    public void DeletePage_RefusesWhenOperationalHistoryExists_PointingToEndService()
    {
        var page = ReadWeb("Pages", "Employees", "Delete.cshtml.cs");

        Assert.Contains("EmployeeOperationalHistoryGuard.FindFirstNonEmptyAsync", page);
        Assert.Contains("إنهاء الخدمة", page);
    }

    /// <summary>
    /// حذفٌ ناعمٌ (IsDeleted=1) للسجلّ الباطل، و**بلا تحويلٍ تلقائيّ** لإنهاء الخدمة —
    /// الأخير يلزمه تاريخ/سبب/نوعٌ يقرّرها الإنسان، فلا تُخترَع بالحذف.
    /// </summary>
    [Fact]
    public void DeletePage_SoftDeletes_AndNeverAutoCreatesEndService()
    {
        var page = ReadWeb("Pages", "Employees", "Delete.cshtml.cs");

        Assert.Contains("IsDeleted = 1", page);
        Assert.DoesNotContain("INSERT INTO EmployeeEndOfService", page);
    }
}
