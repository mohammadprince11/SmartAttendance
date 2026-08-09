using SmartAttendance.Application.Common.Security;
using SmartAttendance.Web.Infrastructure.Imports;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// P0 — فرض النطاق على <b>كتابة الاستيراد</b> بمحرّك التأسيس.
///
/// <para>قبل الإصلاح كان <c>ImportAsync</c> محروساً بصلاحية Import العالمية فقط،
/// فمستخدمٌ مقيَّد النطاق كان يستطيع: تحديث موظفٍ بشركةٍ أخرى (بمطابقة EmployeeNo
/// عالميّاً) بل نقله عابراً للشركات متخطّياً حارس ChangeAssignment؛ وإنشاء بنيةٍ
/// وموظفين خارج نطاقه. القرار المعتمد: فرضٌ صارم — بنطاقٍ مقيَّد لا إنشاء بنية جديدة،
/// ولا لمس وجهةٍ أو موظفٍ خارج النطاق.</para>
///
/// <para>تختبر هذه الحالات قرار الصفّ النقيّ <see cref="EmployeeBootstrapImportEngine.EvaluateRowScope"/>
/// الذي يستعمله الفرض حرفياً، ودلالةَ التقاطع التي تُغذّيه عبر
/// <see cref="PeopleDataScope.AllowsEmployee"/>.</para>
/// </summary>
public sealed class EmployeeImportScopeTests
{
    private const int CompanyA = 1;
    private const int CompanyB = 2;
    private const int BranchInA = 10;
    private const int BranchInB = 20;
    private const int DeptInA = 100;
    private const int DeptInB = 200;
    private const int EmpInB = 900;

    /// <summary>نطاق مقيَّد بشركة A (كمدير موارد بشرية بشركته).</summary>
    private static PeopleDataScope ScopedToA() =>
        AccessRoleScopeTranslator.ToPeopleDataScope(
            "OwnCompany",
            new AccessRoleScopeTranslator.UserAnchors(CompanyA, BranchInA, DeptInA, EmployeeId: 5));

    private static bool AllowedInA(int companyId, int branchId, int deptId) =>
        ScopedToA().AllowsLocation(companyId, branchId, deptId);

    // ===== القرار النقيّ (EvaluateRowScope) =====

    [Fact]
    public void Unrestricted_AlwaysPasses()
    {
        // النطاق العام (أدمن): استيرادٌ تأسيسيٌّ كامل — لا رفض حتى مع بنيةٍ مخطَّطة.
        var error = EmployeeBootstrapImportEngine.EvaluateRowScope(
            isUnrestricted: true,
            anyStructurePlanned: true,
            targetAllowed: false,
            isUpdate: true,
            existingFound: true,
            existingAllowed: false);

        Assert.Null(error);
    }

    [Fact]
    public void Scoped_RejectsNewStructure()
    {
        var error = EmployeeBootstrapImportEngine.EvaluateRowScope(
            isUnrestricted: false,
            anyStructurePlanned: true,
            targetAllowed: false,
            isUpdate: false,
            existingFound: false,
            existingAllowed: false);

        Assert.Equal(EmployeeBootstrapImportEngine.ImportStructureScopeError, error);
    }

    [Fact]
    public void Scoped_RejectsOutOfScopeDestination()
    {
        var error = EmployeeBootstrapImportEngine.EvaluateRowScope(
            isUnrestricted: false,
            anyStructurePlanned: false,
            targetAllowed: false,   // الوجهة خارج النطاق
            isUpdate: false,
            existingFound: false,
            existingAllowed: false);

        Assert.Equal(EmployeeBootstrapImportEngine.ImportDestinationScopeError, error);
    }

    [Fact]
    public void Scoped_RejectsCrossCompanyHijackOnUpdate()
    {
        // الوجهة داخل النطاق، لكن الموظف القائم المطابَق بـEmployeeNo بشركةٍ أخرى.
        var error = EmployeeBootstrapImportEngine.EvaluateRowScope(
            isUnrestricted: false,
            anyStructurePlanned: false,
            targetAllowed: true,
            isUpdate: true,
            existingFound: true,
            existingAllowed: false);

        Assert.Equal(EmployeeBootstrapImportEngine.ImportHijackScopeError, error);
    }

    [Fact]
    public void Scoped_AllowsInScopeCreate()
    {
        var error = EmployeeBootstrapImportEngine.EvaluateRowScope(
            isUnrestricted: false,
            anyStructurePlanned: false,
            targetAllowed: true,
            isUpdate: false,
            existingFound: false,
            existingAllowed: false);

        Assert.Null(error);
    }

    [Fact]
    public void Scoped_AllowsInScopeUpdate()
    {
        var error = EmployeeBootstrapImportEngine.EvaluateRowScope(
            isUnrestricted: false,
            anyStructurePlanned: false,
            targetAllowed: true,
            isUpdate: true,
            existingFound: true,
            existingAllowed: true);

        Assert.Null(error);
    }

    // ===== دلالة التقاطع التي تُغذّي القرار =====

    [Fact]
    public void DestinationInOwnCompany_IsAllowed() =>
        Assert.True(AllowedInA(CompanyA, BranchInA, DeptInA));

    [Fact]
    public void DestinationInAnotherCompany_IsRejected() =>
        Assert.False(AllowedInA(CompanyB, BranchInB, DeptInB));

    [Fact]
    public void ExistingEmployeeInAnotherCompany_IsOutOfScope()
    {
        // منع الاختطاف: موظف B القائم لا يقع ضمن نطاق مستخدم A مهما طابق EmployeeNo.
        var scope = ScopedToA();
        Assert.False(scope.AllowsEmployee(EmpInB, CompanyB, BranchInB, DeptInB));
    }

    [Fact]
    public void ImportScope_Unrestricted_CarriesCompensationFlag()
    {
        Assert.True(EmployeeBootstrapImportEngine.ImportScope.Unrestricted(true).CanEditCompensation);
        Assert.False(EmployeeBootstrapImportEngine.ImportScope.Unrestricted(false).CanEditCompensation);
        Assert.True(EmployeeBootstrapImportEngine.ImportScope.Unrestricted(true).IsUnrestricted);
    }
}
