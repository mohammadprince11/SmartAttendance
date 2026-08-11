using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حرّاس عزل جداول <b>التهيئة</b> (8D–8M): أنواع المناوبات · القواعد · الاستطلاعات.
/// قرار المالك 2026-08-11: **per-tenant**. ولأنّ صفوفها القائمة سبقت عمود الشركة،
/// الدلالة المعتمَدة <c>CompanyId IS NULL = مشتركة</c> فلا تختفي تهيئةٌ مستعملة عن
/// دورٍ مقيَّد (تعطيلٌ حيّ)، بينما الصفوف الجديدة المنسوبة لشركة تُعزَل.
///
/// <para>السلوك مُثبَت أيضاً على قاعدة الاختبار `SmartAttendance_Test`: نطاق شركة 1
/// لا يرى صفوف 2/3، وشركة 2 ترى المشترك + صفّها فقط.</para>
/// </summary>
public sealed class ConfigTenantScopeTests
{
    [Fact]
    public void Unrestricted_SeesEverything()
    {
        var scope = CompanyScope.Unrestricted();
        Assert.Equal("1 = 1", scope.ToSharedConfigSqlPredicate("CompanyId"));
    }

    [Fact]
    public void Restricted_SeesSharedPlusOwn()
    {
        var scope = CompanyScope.ForCompanies(new[] { 2 });
        var sql = scope.ToSharedConfigSqlPredicate("CompanyId");

        // المشترك (NULL) يبقى مرئيّاً — وإلا تعطّلت التهيئة القائمة.
        Assert.Contains("CompanyId IS NULL", sql);
        Assert.Contains("CompanyId IN (2)", sql);
    }

    [Fact]
    public void Restricted_DoesNotLeakOtherCompanies()
    {
        var sql = CompanyScope.ForCompanies(new[] { 1 }).ToSharedConfigSqlPredicate("CompanyId");
        Assert.DoesNotContain("IN (1, 2", sql);
        Assert.DoesNotContain("(2)", sql);
    }

    [Fact]
    public void DeniedAll_SeesSharedOnly_NeverAnyCompanyRow()
    {
        // فشل مغلق: المحروم لا يرى صفّاً منسوباً لأي شركة، ويبقى المشترك فقط.
        var sql = CompanyScope.ForCompanies(Array.Empty<int>()).ToSharedConfigSqlPredicate("CompanyId");
        Assert.Equal("CompanyId IS NULL", sql);
    }

    [Fact]
    public void SharedConfigPredicate_IsLooserThanStrictPredicate()
    {
        // الفرق المقصود عن ToSqlPredicate: هذا يضيف المشترك، وذاك يحصر بالشركة فقط.
        var scope = CompanyScope.ForCompanies(new[] { 3 });
        Assert.DoesNotContain("IS NULL", scope.ToSqlPredicate("CompanyId"));
        Assert.Contains("IS NULL", scope.ToSharedConfigSqlPredicate("CompanyId"));
    }

    [Fact]
    public void Migration_AddsCompanyIdToAllFourConfigTables()
    {
        var migrator = System.IO.File.ReadAllText(
            System.IO.Path.Combine(RepoRoot(), "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs"));

        Assert.Contains("20260811-01-config-tables-company-id", migrator);
        Assert.Contains("ALTER TABLE ShiftTypes ADD CompanyId int NULL", migrator);
        Assert.Contains("ALTER TABLE ShiftRules ADD CompanyId int NULL", migrator);
        Assert.Contains("ALTER TABLE PeriodRules ADD CompanyId int NULL", migrator);
        Assert.Contains("ALTER TABLE EmployeePolls ADD CompanyId int NULL", migrator);
        // الفهارس بمفتاح منفصل: SQL Server لا يرى عموداً أُضيف بنفس الدفعة.
        Assert.Contains("20260811-02-config-tables-company-id-indexes", migrator);
    }

    [Fact]
    public void ShiftTypesPage_ScopesListAndGuardsWrites()
    {
        var page = System.IO.File.ReadAllText(System.IO.Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Pages", "ShiftTypes", "Index.cshtml.cs"));

        Assert.Contains("ICompanyScopeProvider", page);
        Assert.Contains("ListInScopeAsync", page);
        // كلا مساري الكتابة (تعديل + حذف) يمرّان بحارس الملكية قبل المتجر.
        Assert.Contains("IsInScopeAsync", page);
        Assert.Contains("AssignCompanyAsync", page);
        Assert.DoesNotContain("ShiftTypeStore.ListAsync(_dbContext)", page);
    }

    [Fact]
    public void EngineListPath_StaysUnscoped_SoPayrollIsNotBroken()
    {
        // ⚠️ حارس عكسيّ مقصود: ListAsync يخدم محرّك الحضور/الرواتب الذي يحتسب لكل
        // موظف؛ حصرُه بنطاق المتصفّح يُسقط مناوبات من الاحتساب ويُفسد المسير.
        var store = System.IO.File.ReadAllText(System.IO.Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Infrastructure", "Hrms", "ShiftTypeStore.cs"));

        Assert.Contains("public static async Task<List<ShiftType>> ListAsync(ApplicationDbContext dbContext)", store);
        Assert.Contains("ListInScopeAsync", store);
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
