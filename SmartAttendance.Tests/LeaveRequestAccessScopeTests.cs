using System.Text.RegularExpressions;
using SmartAttendance.Application.LeaveRequests.Services;

namespace SmartAttendance.Tests;

/// <summary>
/// انحدار AUTHZ-003 — الثغرة المُغلقة: أيّ موظّف مصادَق كان يقرأ ويحذف **أي طلب
/// إجازة بأي شركة** بمعرفة رقمه، لأن الخدمة عملت بالمعرّف وحده والحارس المركزي
/// يعرف المسار والدور لا المورد.
///
/// الاختبارات هنا على <see cref="LeaveRequestAccessScope"/> النقيّ (قرار السماح)،
/// وسقّاطةٌ نصيّة تمنع عودة النمط القديم لأي صفحة.
/// </summary>
public class LeaveRequestAccessScopeTests
{
    private const int CompanyA = 11;
    private const int CompanyB = 22;
    private const int EmployeeInA = 101;
    private const int EmployeeInB = 202;

    // ─────────────────────────── قرار النطاق ───────────────────────────

    [Fact]
    public void ScopedUser_CannotReachAnotherCompanysRequest()
    {
        var scope = LeaveRequestAccessScope.ForCompanies(new[] { CompanyA });

        Assert.True(scope.Allows(CompanyA, EmployeeInA));
        Assert.False(scope.Allows(CompanyB, EmployeeInB));
    }

    [Fact]
    public void UnrestrictedAdmin_ReachesEveryCompany()
    {
        var scope = LeaveRequestAccessScope.Unrestricted();

        Assert.True(scope.Allows(CompanyA, EmployeeInA));
        Assert.True(scope.Allows(CompanyB, EmployeeInB));
        Assert.True(scope.Allows(null, EmployeeInA));
    }

    [Fact]
    public void EmployeeWithoutCompany_IsRefusedForScopedUsers()
    {
        // موظّفٌ بلا شركة لا تُعرف تبعيّته — لا يُسلَّم لمستخدمٍ مقيَّد بشركة.
        var scope = LeaveRequestAccessScope.ForCompanies(new[] { CompanyA });

        Assert.False(scope.Allows(null, EmployeeInA));
    }

    [Fact]
    public void DeniedAll_RefusesEverything()
    {
        var scope = LeaveRequestAccessScope.DeniedAll();

        Assert.True(scope.IsDeniedAll);
        Assert.False(scope.Allows(CompanyA, EmployeeInA));
        Assert.False(scope.Allows(null, EmployeeInA));
    }

    [Fact]
    public void EmployeeRestriction_BlocksColleaguesInTheSameCompany()
    {
        // الحالة الأصلية للثغرة: زميلٌ بنفس الشركة كان يُحذف طلبه.
        var scope = LeaveRequestAccessScope
            .ForCompanies(new[] { CompanyA })
            .RestrictedToEmployee(EmployeeInA);

        Assert.True(scope.Allows(CompanyA, EmployeeInA));
        Assert.False(scope.Allows(CompanyA, 999));
    }

    [Fact]
    public void EmployeeRestriction_StacksOnTopOfCompany_NeverReplacesIt()
    {
        // قيد الموظّف **يضاف** ولا يُلغي قيد الشركة: نفس رقم الموظّف بشركة أخرى يُرفض.
        var scope = LeaveRequestAccessScope
            .ForCompanies(new[] { CompanyA })
            .RestrictedToEmployee(EmployeeInA);

        Assert.False(scope.Allows(CompanyB, EmployeeInA));
    }

    [Fact]
    public void UnrestrictedAdmin_ActingAsEmployee_IsStillRestrictedToOwnRequests()
    {
        var scope = LeaveRequestAccessScope.Unrestricted().RestrictedToEmployee(EmployeeInA);

        Assert.True(scope.Allows(CompanyA, EmployeeInA));
        Assert.False(scope.Allows(CompanyA, 999));
    }

    [Fact]
    public void InvalidEmployeeId_CollapsesToDeniedAll()
    {
        // هوية ناقصة لا تُترجَم لصلاحية أوسع — مغلق الفشل.
        var scope = LeaveRequestAccessScope.ForCompanies(new[] { CompanyA }).RestrictedToEmployee(0);

        Assert.True(scope.IsDeniedAll);
        Assert.False(scope.Allows(CompanyA, EmployeeInA));
    }

    [Fact]
    public void EmptyCompanyList_IsDeniedAll_NotAllowAll()
    {
        var scope = LeaveRequestAccessScope.ForCompanies(Array.Empty<int>());

        Assert.True(scope.IsDeniedAll);
        Assert.False(scope.Allows(CompanyA, EmployeeInA));
    }

    // ─────────────────────── سقّاطة: لا عودة للنمط القديم ───────────────────────

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    [Fact]
    public void EveryServiceMethod_DemandsAnAccessScope()
    {
        // العقد نفسه هو الحارس: معطىً إلزاميّ بلا قيمة افتراضية ⟹ المُصرِّف يرفض
        // أي استدعاء جديد ينسى النطاق. لو عاد أحدهم فأعطاه قيمة افتراضية
        // (`= null`) لسقط الحارس صامتاً — هذا ما يمنعه هذا الاختبار.
        var contract = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "SmartAttendance.Application", "LeaveRequests", "Services", "ILeaveRequestService.cs"));

        var methods = Regex.Matches(contract, @"Task<[^>]+>\s+\w+Async\((?<args>[^;]*?)\);",
            RegexOptions.Singleline);

        Assert.NotEmpty(methods);

        foreach (Match method in methods)
        {
            var args = method.Groups["args"].Value;

            Assert.Contains("LeaveRequestAccessScope scope", args);
            Assert.DoesNotContain("LeaveRequestAccessScope scope =", args);
        }
    }

    [Fact]
    public void NoLeaveRequestPage_CallsTheServiceWithoutBuildingAScope()
    {
        var pagesDirectory = Path.Combine(
            RepositoryRoot(), "SmartAttendance.Web", "Pages", "LeaveRequests");

        var offenders = new List<string>();

        foreach (var page in Directory.GetFiles(pagesDirectory, "*.cshtml.cs"))
        {
            var source = File.ReadAllText(page);

            if (!source.Contains("_leaveRequestService.")) continue;

            if (!source.Contains("LeaveRequestScopeFactory"))
            {
                offenders.Add(Path.GetFileName(page));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "صفحات تستدعي خدمة الإجازات بلا بناء نطاق: " + string.Join(", ", offenders));
    }
}
