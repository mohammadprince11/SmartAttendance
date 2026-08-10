using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حرّاس انحدار لعزل الشركات على الشاشات التي كانت تقبل معرّفاً من الطلب وتكتب بلا
/// فحص ملكية (مسح 2026-08-11، فرع security/full-production-closure). كل صفحة أدناه
/// كانت مكشوفةً: مستخدم شركة A يقرأ/يكتب موارد شركة B عبر معرّف مباشر أو نموذج معدَّل.
///
/// <para>هذه الاختبارات تفشل قبل الإصلاح (سلسلة الحارس غائبة) وتنجح بعده — بلا حاجة
/// لقاعدة بيانات حيّة، فهي حرّاس نصّيّة على وجود استدعاء الحارس بمسارات الكتابة.
/// الإثبات السلوكيّ (A لا يعدّل B) محلّه اختبارات التكامل عند توفّر قاعدة اختبار.</para>
/// </summary>
public sealed class TenantIsolationGuardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Page(params string[] segments)
    {
        var parts = new List<string> { RepoRoot(), "SmartAttendance.Web", "Pages" };
        parts.AddRange(segments);
        return File.ReadAllText(Path.Combine(parts.ToArray()));
    }

    private static string Security(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "SmartAttendance.Web", "Infrastructure", "Security", file));

    [Fact]
    public void CentralGuard_ExposesBulkScopePrimitives()
    {
        var guard = Security("EmployeeCompanyGuard.cs");
        Assert.Contains("FilterEmployeesInScopeAsync", guard);
        Assert.Contains("FilterOwnedRowsInScopeAsync", guard);
    }

    [Fact]
    public void ShiftAssignments_GuardsBulkEmployeeWrites()
    {
        var page = Page("ShiftAssignments", "Index.cshtml.cs");
        Assert.Contains("ICompanyScopeProvider", page);
        Assert.Contains("FilterEmployeesInScopeAsync", page);
        // كلا مساري الكتابة يستدعيان الحارس قبل المتجر.
        Assert.Contains("AllInScopeAsync", page);
    }

    [Fact]
    public void EmployeeGeoLocations_GuardsBulkEmployeeWrites()
    {
        var page = Page("EmployeeGeoLocations", "Index.cshtml.cs");
        Assert.Contains("ICompanyScopeProvider", page);
        Assert.Contains("FilterEmployeesInScopeAsync", page);
        Assert.Contains("AllInScopeAsync", page);
    }

    [Fact]
    public void ShiftOverrides_ScopesAllAndGuardsDelete()
    {
        var page = Page("ShiftOverrides", "Index.cshtml.cs");
        Assert.Contains("ICompanyScopeProvider", page);
        // «الكل» + التحديد الصريح يمرّان بحصر النطاق؛ والحذف بمعرّف صفٍّ يمرّ بحارس الملكية.
        Assert.Contains("FilterEmployeesInScopeAsync", page);
        Assert.Contains("FilterOwnedRowsInScopeAsync", page);
    }

    [Fact]
    public void BiometricKeys_GuardsApproveRejectRevokeByOwnership()
    {
        var page = Page("BiometricKeys", "Index.cshtml.cs");
        Assert.Contains("ICompanyScopeProvider", page);
        Assert.Contains("CanAccessOwnedRowAsync", page);
        // الاعتماد/الرفض/الإلغاء كلها محروسة.
        Assert.Contains("CanAccessCredentialAsync", page);
    }

    [Fact]
    public void AssetsManagement_GuardsMarkReturnedByOwnership()
    {
        var page = Page("AssetsManagement", "Index.cshtml.cs");
        Assert.Contains("ICompanyScopeProvider", page);
        Assert.Contains("CanAccessOwnedRowAsync", page);
        Assert.Contains("FilterEmployeesInScopeAsync", page);
    }

    [Fact]
    public void EmployeeTasks_GuardsLaunchAndTaskActions()
    {
        var page = Page("EmployeeTasks", "Index.cshtml.cs");
        Assert.Contains("ICompanyScopeProvider", page);
        // إطلاق العملية يفحص ملكية الموظف؛ إجراءات المهمة تفحص ملكية الصفّ.
        Assert.Contains("CanAccessEmployeeAsync", page);
        Assert.Contains("CanAccessTaskAsync", page);
    }
}
