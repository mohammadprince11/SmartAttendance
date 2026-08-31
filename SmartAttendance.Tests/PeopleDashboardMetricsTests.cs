using System;
using System.IO;
using SmartAttendance.Web.Pages.Employees;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// دلالة مؤشّرات داشبورد الأشخاص (People — Task 5). العزل منجزٌ سابقاً؛ هذه تصحّح
/// **المعنى**: التجربة من الإعداد لا 90 صلبة · الموقوف حالةٌ حقيقيّة لا IsActive=0 ·
/// «جديد هذا الشهر» لا يشمل المستقبل · مقام الدوران متوسّط القوى العاملة الموثَّق.
/// </summary>
public class PeopleDashboardMetricsTests
{
    // ═══ تحويل مدة التجربة (نقيّ) ═══

    [Theory]
    [InlineData("Day", 90, false, 0, 90)]
    [InlineData("Day", 30, false, 0, 30)]
    [InlineData("Month", 3, false, 0, 90)]     // شهر ≈ 30 يوماً
    [InlineData("Week", 2, false, 0, 14)]      // أسبوع = 7
    [InlineData("Day", 90, true, 15, 105)]     // التمديد يُضاف حين يُسمح
    [InlineData("Day", 90, false, 15, 90)]     // ولا يُضاف حين لا يُسمح
    public void ProbationDaysFromPolicy_ConvertsUnitAndExtension(
        string unit, int value, bool allowExt, int extDays, int expected)
    {
        Assert.Equal(expected, PeopleDashboardModel.ProbationDaysFromPolicy(unit, value, allowExt, extDays));
    }

    [Fact]
    public void ProbationDaysFromPolicy_NeverBelowOneDay()
    {
        Assert.Equal(1, PeopleDashboardModel.ProbationDaysFromPolicy("Day", 0, false, 0));
        Assert.Equal(1, PeopleDashboardModel.ProbationDaysFromPolicy("Day", -5, false, 0));
    }

    // ═══ الحارس النصّيّ: تعريفات الاستعلام ═══

    private static string Dashboard()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "SmartAttendance.Web", "Pages", "Employees", "PeopleDashboard.cshtml.cs"));
    }

    [Fact]
    public void NewThisMonth_ExcludesFutureHires()
    {
        var sql = Dashboard();
        // محدودٌ من بداية الشهر حتى اليوم — لا تعيينٌ مستقبليّ.
        Assert.Contains("HireDate >= @MonthStart AND HireDate <= @Today", sql);
    }

    [Fact]
    public void Probation_UsesConfiguredDays_NotHardcoded90()
    {
        var sql = Dashboard();
        Assert.Contains("DATEADD(day, @ProbationDays,", sql);
        // لا يبقى الرقم الصلب 90 داخل تعريف مؤشّر التجربة.
        Assert.DoesNotContain("DATEADD(day, 90,", sql);
    }

    [Fact]
    public void Suspended_UsesRealEmploymentStatus_NotInactiveFlag()
    {
        var sql = Dashboard();
        var at = sql.IndexOf("Suspended        =", StringComparison.Ordinal);
        Assert.True(at > 0, "تعريف مؤشّر الموقوف غير موجود.");

        var definition = sql[at..(sql.IndexOf("EndedThisMonth", at, StringComparison.Ordinal))];
        Assert.Contains("EmploymentStatus", definition);
        Assert.Contains("IsActive = 1", definition);          // موظفٌ فعّالٌ موقوف
        Assert.DoesNotContain("IsActive = 0", definition);    // لا كلّ منتهي الخدمة
    }

    [Fact]
    public void Turnover_DenominatorIsDocumentedAverageHeadcount()
    {
        var sql = Dashboard();
        // المقام متوسّط القوى العاملة (الفعّالون + نصف المغادرين) لا عدد الفعّالين وحده.
        Assert.Contains("ActiveEmployees + exits / 2.0", sql);
        Assert.Contains("averageHeadcount", sql);
    }

    [Fact]
    public void Dashboard_EnsuresLifecycleSchemaBeforeQueryingEndServices()
    {
        var source = Dashboard();
        var ensure = source.IndexOf("EmployeeLifecycleSchema.EnsureAsync(_dbContext)", StringComparison.Ordinal);
        var query = source.IndexOf("var rows = await HrmsDatabase.QueryAsync", StringComparison.Ordinal);

        Assert.True(ensure >= 0, "لوحة الموظفين لا تهيئ مخطط دورة حياة الموظف.");
        Assert.True(query > ensure, "يجب إنشاء جدول إنهاء الخدمات قبل أول استعلام للوحة.");
    }
}
