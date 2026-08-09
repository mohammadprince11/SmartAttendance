using System;
using System.IO;
using System.Linq;
using SmartAttendance.Application.Common.Security;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Lookup SQL-first (People — Task 3): تطبيق النطاق **قبل** حدّ TOP لا بعده.
///
/// <para><b>ما كان</b>: نقطة <c>/employees/lookup</c> تقرأ TOP(50) ثمّ تُخوِّل
/// صفّاً-صفّاً (N+1). فإن وقع أوّل الخمسين كلُّهم خارج نطاق المستخدم، رأى المستخدم
/// **صفراً** رغم أنّ له مخوّلين خلفهم — الحدُّ ابتلع المرفوضين. الإصلاح يطبّق
/// <see cref="PeopleDataScopeQueryExtensions.ApplyPeopleDataScope"/> مرّتين (قواعد ∩
/// أدوار وصول) **داخل الاستعلام قبل TOP**، بنفس مرشِّح قائمة الأشخاص (P0-1).</para>
///
/// <para>هذه الاختبارات تثبّت الدلالة على <c>IQueryable&lt;Employee&gt;</c> بالذاكرة —
/// نفس سلسلة العمليات التي يبنيها المعالج. دلالة التقاطع نفسها محروسة أيضاً بـ
/// <see cref="EmployeeListScopeIntersectionTests"/>.</para>
/// </summary>
public sealed class EmployeeLookupScopeBeforeLimitTests
{
    private const int CompanyA = 1;
    private const int CompanyB = 2;
    private const int BranchA = 10;
    private const int BranchB = 20;

    /// <summary>
    /// دليلٌ مختلط: <paramref name="countB"/> موظفاً بالشركة B أرقامُهم تسبق (B####)،
    /// و<paramref name="countA"/> بالشركة A أرقامُهم تليها (Z####) — فيقعون **خلف**
    /// حدّ TOP بالترتيب الأبجديّ. هذا يُظهر الفرق بين «النطاق قبل الحدّ» و«بعده».
    /// </summary>
    private static IQueryable<Employee> MixedDirectory(int countB, int countA)
    {
        var branchA = new Branch { Id = BranchA, CompanyId = CompanyA };
        var branchB = new Branch { Id = BranchB, CompanyId = CompanyB };

        var list = new System.Collections.Generic.List<Employee>();
        for (var i = 1; i <= countB; i++)
            list.Add(new Employee { Id = 20000 + i, EmployeeNo = $"B{i:0000}", BranchId = BranchB, Branch = branchB, DepartmentId = 200, IsActive = true, IsDeleted = false });
        for (var i = 1; i <= countA; i++)
            list.Add(new Employee { Id = 10000 + i, EmployeeNo = $"Z{i:0000}", BranchId = BranchA, Branch = branchA, DepartmentId = 100, IsActive = true, IsDeleted = false });

        return list.AsQueryable();
    }

    private static PeopleDataScope CompanyScoped(int companyId) => new()
    {
        IsUnrestricted = false,
        AllowedCompanyIds = new[] { companyId }
    };

    /// <summary>نفس سلسلة المعالج: نطاقان ثمّ ترتيبٌ ثمّ حدّ.</summary>
    private static System.Collections.Generic.List<string> RunLookup(
        IQueryable<Employee> directory, PeopleDataScope rules, PeopleDataScope access, int take) =>
        directory
            .Where(e => !e.IsDeleted)
            .ApplyPeopleDataScope(rules)
            .ApplyPeopleDataScope(access)
            .Where(e => e.IsActive)
            .OrderBy(e => e.EmployeeNo)
            .Take(take + 1)
            .Select(e => e.EmployeeNo)
            .ToList();

    [Fact]
    public void TakeBeforeScope_ReturnsZeroAuthorized_RedFirst()
    {
        // المسلك القديم: TOP أوّلاً (كلُّهم B) ثمّ التخويل (A فقط) ⟹ لا شيء.
        var directory = MixedDirectory(countB: 60, countA: 5);

        var takeFirstThenScope = directory
            .Where(e => !e.IsDeleted)
            .OrderBy(e => e.EmployeeNo)
            .Take(51)                                  // الحدّ قبل النطاق
            .Where(e => e.Branch.CompanyId == CompanyA) // التخويل بعده
            .Select(e => e.EmployeeNo)
            .ToList();

        Assert.Empty(takeFirstThenScope); // الثغرة: مخوّلون موجودون لكنّ الحدّ ابتلع المرفوضين.
    }

    [Fact]
    public void ScopeBeforeTake_SurfacesAuthorizedRowsBehindTheLimit()
    {
        // الإصلاح: النطاق أوّلاً ⟹ الخمسة المخوّلون يظهرون رغم وقوعهم خلف حدّ TOP.
        var directory = MixedDirectory(countB: 60, countA: 5);

        var rows = RunLookup(directory, PeopleDataScope.Unrestricted(), CompanyScoped(CompanyA), take: 50);

        Assert.Equal(5, rows.Count);
        Assert.All(rows, no => Assert.StartsWith("Z", no));
    }

    [Fact]
    public void ScopeBeforeTake_FillsUpToTake_WithAuthorizedOnly()
    {
        // 60 مخوّلاً بالشركة A، الحدّ 50 ⟹ يعيد 51 (صفٌّ زائدٌ للكشف عن المزيد) كلُّهم مخوّلون.
        var directory = MixedDirectory(countB: 40, countA: 60);

        var rows = RunLookup(directory, PeopleDataScope.Unrestricted(), CompanyScoped(CompanyA), take: 50);

        Assert.Equal(51, rows.Count);                 // probe = take + 1
        Assert.All(rows, no => Assert.StartsWith("Z", no));
    }

    [Fact]
    public void BothScopesIntersect_RulesAndAccessRole()
    {
        // قواعد: الشركتان. أدوار الوصول: A. التقاطع ⟹ A فقط (نفس قائمة الأشخاص).
        var directory = MixedDirectory(countB: 5, countA: 5);
        var rules = new PeopleDataScope { IsUnrestricted = false, AllowedCompanyIds = new[] { CompanyA, CompanyB } };

        var rows = RunLookup(directory, rules, CompanyScoped(CompanyA), take: 50);

        Assert.All(rows, no => Assert.StartsWith("Z", no));
        Assert.Equal(5, rows.Count);
    }

    // ═══ حارس نصّيّ: المعالج يطبّق النطاق داخل الاستعلام بلا N+1 ═══

    private static string LookupSource()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "SmartAttendance.Web", "Pages", "Employees", "Lookup.cshtml.cs"));
    }

    [Fact]
    public void Lookup_AppliesScopeInQuery_BeforeTheLimit()
    {
        var source = LookupSource();

        var scopeAt = source.IndexOf("ApplyPeopleDataScope", StringComparison.Ordinal);
        var takeAt = source.IndexOf(".Take(", StringComparison.Ordinal);

        Assert.True(scopeAt > 0, "النطاق لا يُطبَّق داخل الاستعلام.");
        Assert.True(takeAt > scopeAt, "الحدّ (Take) يجب أن يقع بعد تطبيق النطاق، لا قبله.");
    }

    [Fact]
    public void Lookup_HasNoPerRowAuthorizationLoop()
    {
        // القصْر صار داخل SQL: لا نداء CanAccessEmployeeAsync لكلّ صفّ (N+1).
        Assert.DoesNotContain("CanAccessEmployeeAsync", LookupSource());
    }
}
