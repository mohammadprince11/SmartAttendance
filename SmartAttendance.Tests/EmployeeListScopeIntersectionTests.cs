using SmartAttendance.Application.Common.Security;
using SmartAttendance.Domain.Entities;
using SmartAttendance.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// P0-1 — تقاطع نطاق القواعد مع نطاق أدوار الوصول على <b>عضوية قائمة الأشخاص</b>.
///
/// <para>قبل الإصلاح كان <c>GetPagedAsync</c> يطبّق نطاق القواعد وحده على الاستعلام،
/// ونطاق أدوار الوصول يُقيَّم صفّاً-صفّاً لبوّابة رابط الملف فقط — فمن نطاق أدواره
/// أضيق من نطاق قواعده كان يرى <b>أسطر</b> موظفين خارج نطاقه بالدليل. الإصلاح يطبّق
/// <see cref="PeopleDataScopeQueryExtensions.ApplyPeopleDataScope"/> مرّتين (تقاطع AND)
/// بنفس المرشِّح القابل للترجمة لـSQL.</para>
///
/// <para>هذه الاختبارات تثبّت <b>دلالة التقاطع</b> التي يبنيها السطر المُضاف حرفياً:
/// تسلسل مرشِّحَي النطاق على <c>IQueryable&lt;Employee&gt;</c> يساوي AND، فيحصر الناتج
/// بما يسمح به الاثنان معاً. أحمر أولاً: بتطبيق نطاق القواعد وحده (شركتان) يظهر موظف
/// الشركة الأخرى — أي الثغرة.</para>
/// </summary>
public sealed class EmployeeListScopeIntersectionTests
{
    private const int CompanyA = 1;
    private const int CompanyB = 2;
    private const int BranchInA = 10;
    private const int BranchInB = 20;
    private const int DeptInA = 100;
    private const int DeptInB = 200;
    private const int EmpInA = 1001;
    private const int EmpInB = 1002;

    /// <summary>موظفان: واحدٌ بكل شركة، مع تعبئة تنقّل <c>Branch</c> (منه تُشتقّ الشركة).</summary>
    private static IQueryable<Employee> TwoCompanyDirectory()
    {
        var branchA = new Branch { Id = BranchInA, CompanyId = CompanyA };
        var branchB = new Branch { Id = BranchInB, CompanyId = CompanyB };

        return new List<Employee>
        {
            new() { Id = EmpInA, BranchId = BranchInA, Branch = branchA, DepartmentId = DeptInA, IsDeleted = false },
            new() { Id = EmpInB, BranchId = BranchInB, Branch = branchB, DepartmentId = DeptInB, IsDeleted = false }
        }.AsQueryable();
    }

    /// <summary>نطاق يسمح بشركةٍ بعينها فقط (كنطاق القواعد أو أدوار الوصول المقيَّد).</summary>
    private static PeopleDataScope CompanyScoped(int companyId) => new()
    {
        IsUnrestricted = false,
        AllowedCompanyIds = new[] { companyId }
    };

    [Fact]
    public void RulesScopeAlone_LeaksOtherCompanyRow_RedFirst()
    {
        // نطاق القواعد يسمح بالشركتين — بلا تقاطع يظهر موظف B (الثغرة قبل الإصلاح).
        var directoryScope = new PeopleDataScope
        {
            IsUnrestricted = false,
            AllowedCompanyIds = new[] { CompanyA, CompanyB }
        };

        var rows = TwoCompanyDirectory()
            .ApplyPeopleDataScope(directoryScope)
            .Select(e => e.Id)
            .ToList();

        Assert.Contains(EmpInB, rows);
    }

    [Fact]
    public void IntersectingWithAccessRoleScope_HidesOtherCompanyRow()
    {
        // نطاق القواعد: الشركتان. نطاق أدوار الوصول: شركة A وحدها. التقاطع ⟹ A فقط.
        var directoryScope = new PeopleDataScope
        {
            IsUnrestricted = false,
            AllowedCompanyIds = new[] { CompanyA, CompanyB }
        };
        var accessRoleScope = CompanyScoped(CompanyA);

        var rows = TwoCompanyDirectory()
            .ApplyPeopleDataScope(directoryScope)
            .ApplyPeopleDataScope(accessRoleScope)   // نفس ما يفعله GetPagedAsync الآن.
            .Select(e => e.Id)
            .ToList();

        Assert.Equal(new[] { EmpInA }, rows);
    }

    [Fact]
    public void UnrestrictedAccessRoleScope_DoesNotTighten()
    {
        // أدمن/«All»: نطاق أدوار الوصول غير مقيَّد ⟹ التقاطع لا يحذف شيئاً.
        var directoryScope = CompanyScoped(CompanyA);
        var accessRoleScope = PeopleDataScope.Unrestricted();

        var rows = TwoCompanyDirectory()
            .ApplyPeopleDataScope(directoryScope)
            .ApplyPeopleDataScope(accessRoleScope)
            .Select(e => e.Id)
            .ToList();

        Assert.Equal(new[] { EmpInA }, rows);   // نطاق القواعد وحده يحصر — لا توسيع.
    }

    [Fact]
    public void DeniedAllAccessRoleScope_HidesEveryRow()
    {
        // حرمانٌ كليّ من جهة أدوار الوصول ⟹ لا صفوف مهما سمح نطاق القواعد.
        var directoryScope = PeopleDataScope.Unrestricted();
        var accessRoleScope = PeopleDataScope.Empty(selfEmployeeId: null);

        var rows = TwoCompanyDirectory()
            .ApplyPeopleDataScope(directoryScope)
            .ApplyPeopleDataScope(accessRoleScope)
            .ToList();

        Assert.Empty(rows);
    }
}
