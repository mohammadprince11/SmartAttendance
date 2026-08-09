using System;
using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// أداء People (Task 6) — سرد موظفي شاشة «تحديثات الموظفين» يطبّق النطاق داخل SQL
/// قبل الحدّ. كان <c>SELECT TOP 500 … ORDER BY FullName</c> ثمّ ترشيحاً بالذاكرة
/// (نفس صنف عطل المنتقي بـTask 3): المقيَّد قد يخسر مخوّلين خلف أوّل خمسمئة، ويُحمَّل
/// 500 صفّاً لترشيحها على 10k+ موظف. الآن EF + <c>ApplyPeopleDataScope</c> مرّتين قبل
/// <c>Take</c>، فالحدّ يقع بعد النطاق ولا ترشيح بالذاكرة. دلالة النطاق نفسها محروسة
/// بـ<see cref="EmployeeListScopeIntersectionTests"/> و<see cref="EmployeeLookupScopeBeforeLimitTests"/>.
/// </summary>
public class EmployeeUpdatesScopeBeforeLimitTests
{
    private static string Source()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "SmartAttendance.Web", "Pages", "EmployeeUpdates", "Index.cshtml.cs"));
    }

    private static string LoadEmployeesBody(string source)
    {
        var at = source.IndexOf("Task<List<UpdateEmployee>> LoadEmployeesAsync", StringComparison.Ordinal);
        Assert.True(at > 0, "LoadEmployeesAsync غير موجود.");
        var end = source.IndexOf("private ", at + 1, StringComparison.Ordinal);
        return source[at..(end > at ? end : source.Length)];
    }

    [Fact]
    public void LoadEmployees_AppliesScopeInQuery_BeforeTheLimit()
    {
        var body = LoadEmployeesBody(Source());

        var scopeAt = body.IndexOf("ApplyPeopleDataScope", StringComparison.Ordinal);
        var takeAt = body.IndexOf(".Take(", StringComparison.Ordinal);

        Assert.True(scopeAt > 0, "النطاق لا يُطبَّق داخل الاستعلام.");
        Assert.True(takeAt > scopeAt, "الحدّ (Take) يجب أن يقع بعد النطاق.");
    }

    [Fact]
    public void LoadEmployees_NoLongerLimitsBeforeScope_OrFiltersInMemory()
    {
        var body = LoadEmployeesBody(Source());

        Assert.DoesNotContain("TOP 500", body);        // لا حدٌّ قبل النطاق بالخام
        Assert.DoesNotContain("AllowsEmployee", body); // لا ترشيح نطاقٍ بالذاكرة
    }

    /// <summary>
    /// منتقي المدراء محصورٌ بشركة الموظف المُحدَّد — لا يكشف أسماء/أرقام موظفي شركةٍ
    /// أخرى للمستخدم المقيَّد (نظير Edit.LoadManagersAsync).
    /// </summary>
    [Fact]
    public void ManagerPicker_IsScopedToTheSelectedEmployeesCompany()
    {
        var source = Source();
        var at = source.IndexOf("Task<List<EmployeeLookupOption>> LoadActiveManagersAsync", StringComparison.Ordinal);
        Assert.True(at > 0, "تعريف LoadActiveManagersAsync غير موجود.");
        var end = source.IndexOf("private ", at + 1, StringComparison.Ordinal);
        var body = source[at..(end > at ? end : source.Length)];

        Assert.Contains("b.CompanyId = (", body);       // محصورٌ بشركة الموظف المُحدَّد
        Assert.Contains("INNER JOIN Branches", body);
    }
}
