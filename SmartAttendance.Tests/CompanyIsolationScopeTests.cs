using SmartAttendance.Application.Common.Security;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// المرحلة 7 — عزل الشركات، بشركتين حقيقيتين بالسيناريو: مستخدم بشركة (أ) لا
/// يقرأ ولا يعدّل ولا ينزّل مستند موظف بشركة (ب).
///
/// ملاحظة صدق مهمّة: هذه الاختبارات تثبّت <b>طبقة التقييم</b> (نطاق البيانات
/// الذي تفرضه نقطة التنزيل المحمية وحارس صفحات الأشخاص)، لا عزلاً بنيوياً على
/// مستوى المخطط — كيان <c>Employee</c> ما زال بلا <c>CompanyId</c> مباشر
/// (تفاصيل بـdocs/COMPANY-ISOLATION-AUDIT.md). لا ندّعي أكثر مما نختبر.
/// </summary>
public class CompanyIsolationScopeTests
{
    private const int CompanyA = 1;
    private const int CompanyB = 2;

    // موظفون: (المعرّف، الشركة، الفرع، القسم)
    private const int EmployeeInA = 100;
    private const int BranchInA = 10;
    private const int DepartmentInA = 1000;

    private const int EmployeeInB = 200;
    private const int BranchInB = 20;
    private const int DepartmentInB = 2000;

    /// <summary>مدير موارد بشرية مقيَّد بشركته (Data scope = OwnCompany).</summary>
    private static PeopleDataScope CompanyScopedHrOfCompanyA() =>
        AccessRoleScopeTranslator.ToPeopleDataScope(
            "OwnCompany",
            new AccessRoleScopeTranslator.UserAnchors(CompanyA, BranchInA, DepartmentInA, EmployeeId: 101));

    [Fact]
    public void UserFromCompanyA_CanReadOwnCompanyEmployee() =>
        Assert.True(CompanyScopedHrOfCompanyA()
            .AllowsEmployee(EmployeeInA, CompanyA, BranchInA, DepartmentInA));

    [Fact]
    public void UserFromCompanyA_CannotReadCompanyBEmployee() =>
        Assert.False(CompanyScopedHrOfCompanyA()
            .AllowsEmployee(EmployeeInB, CompanyB, BranchInB, DepartmentInB));

    /// <summary>
    /// نفس النطاق يحكم القراءة والتعديل والتنزيل: نقطة <c>/files</c> تستدعي
    /// <c>CanAccessEmployeeAsync</c> الذي يقيّم هذا النطاق نفسه، فمنع القراءة
    /// يعني منع تنزيل مستند موظف الشركة الأخرى.
    /// </summary>
    [Fact]
    public void UserFromCompanyA_CannotDownloadCompanyBDocument() =>
        Assert.False(CompanyScopedHrOfCompanyA()
            .AllowsEmployee(EmployeeInB, CompanyB, BranchInB, DepartmentInB));

    [Fact]
    public void CrossCompanyBranchCollision_DoesNotLeak()
    {
        // موظف بشركة (ب) لكن معرّف قسمه يساوي قسماً بشركة (أ) — النطاق يسمح
        // بمطابقة القسم، فهذا الاختبار يوثّق الخطر المعروف صراحةً بدل إخفائه.
        var scope = AccessRoleScopeTranslator.ToPeopleDataScope(
            "OwnCompany",
            new AccessRoleScopeTranslator.UserAnchors(CompanyA, BranchInA, DepartmentInA, 101));

        Assert.False(scope.AllowsEmployee(EmployeeInB, CompanyB, BranchInB, DepartmentInA));
    }

    [Fact]
    public void DeniedCompany_OverridesAllowance()
    {
        var scope = new PeopleDataScope
        {
            IsUnrestricted = true,
            DeniedCompanyIds = new[] { CompanyB }
        };

        Assert.True(scope.AllowsEmployee(EmployeeInA, CompanyA, BranchInA, DepartmentInA));
        Assert.False(scope.AllowsEmployee(EmployeeInB, CompanyB, BranchInB, DepartmentInB));
    }

    [Fact]
    public void EmptyScope_DeniesEveryone()
    {
        var scope = PeopleDataScope.Empty(selfEmployeeId: 101);

        Assert.False(scope.AllowsEmployee(EmployeeInA, CompanyA, BranchInA, DepartmentInA));
        Assert.False(scope.AllowsEmployee(EmployeeInB, CompanyB, BranchInB, DepartmentInB));
    }

    // ===== توكن الموظف (واجهة الموبايل) =====

    /// <summary>
    /// توكن موظف بشركة (ب) لا يصل لموظف آخر: نطاق «Self» يسمح بصاحبه فقط —
    /// وهي نفس القاعدة التي تطبّقها نقطة التنزيل على دور Employee.
    /// </summary>
    [Fact]
    public void EmployeeTokenScope_ReachesSelfOnly()
    {
        var scope = AccessRoleScopeTranslator.ToPeopleDataScope(
            "Self",
            new AccessRoleScopeTranslator.UserAnchors(CompanyB, BranchInB, DepartmentInB, EmployeeInB));

        Assert.True(scope.AllowsEmployee(EmployeeInB, CompanyB, BranchInB, DepartmentInB));
        Assert.False(scope.AllowsEmployee(EmployeeInA, CompanyA, BranchInA, DepartmentInA));

        // ولا حتى زميل بنفس الفرع والقسم.
        Assert.False(scope.AllowsEmployee(EmployeeInB + 1, CompanyB, BranchInB, DepartmentInB));
    }

    // ===== نطاق الفرع/القسم داخل الشركة نفسها =====

    [Fact]
    public void BranchScopedUser_SeesOwnBranchOnly()
    {
        var user = new EmployeeScopeEvaluator.ActingUser(BranchInA, DepartmentInA, 101);

        Assert.True(EmployeeScopeEvaluator.IsInScope(
            "OwnBranch", user, new EmployeeScopeEvaluator.EmployeeRef(EmployeeInA, BranchInA, DepartmentInA)));

        Assert.False(EmployeeScopeEvaluator.IsInScope(
            "OwnBranch", user, new EmployeeScopeEvaluator.EmployeeRef(EmployeeInB, BranchInB, DepartmentInB)));
    }
}
