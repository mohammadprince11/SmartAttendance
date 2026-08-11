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

    // ---- Phase 3: صفحات الهياكل التنظيمية (مُنتقي شركةٍ غير محصور بالنطاق) ----
    // كانت تعرض كل الشركات للمُنتقي وتمرّرها جميعاً لـ Resolve، فيختار دورٌ مقيَّد أي
    // شركة (أو يمرّرها بالرابط) ⟹ تسرّب هيكل/أسماء/سلسلة مدراء شركةٍ أخرى.

    [Fact]
    public void OrganizationIndex_ScopesCompanyPickerAndGuardsStructureWrites()
    {
        var page = Page("Organization", "Index.cshtml.cs");
        Assert.Contains("ICompanyScopeProvider", page);
        // المُنتقيان (البطاقات + شجرة الهيكل) محصوران بالنطاق.
        Assert.Contains("scope.Allows", page);
        // كتابات الهيكل لا تعتمد CompanyId النموذج بلا فحص نطاق.
        Assert.Contains("ApplyCompanyScope", page);
    }

    [Fact]
    public void OrganizationChart_ScopesCompanyPicker()
    {
        var page = Page("Organization", "Chart.cshtml.cs");
        Assert.Contains("ICompanyScopeProvider", page);
        Assert.Contains("scope.Allows", page);
    }

    [Fact]
    public void OrgStructures_ScopesCompanyPicker()
    {
        var page = Page("OrgStructures", "Index.cshtml.cs");
        Assert.Contains("ICompanyScopeProvider", page);
        Assert.Contains("scope.Allows", page);
    }

    // ---- Phase 2: لوحة التحكم (المُنتقي يصله كل دور) ----
    [Fact]
    public void Dashboard_ScopesCompanyPickerAndMetricExecution()
    {
        var page = Page("Index.cshtml.cs");
        Assert.Contains("ICompanyScopeProvider", page);
        Assert.Contains("scope.Allows", page);
    }

    // ---- Phase 21: اعتماد/تجاهل/تعديل الاقتراحات = أثرٌ حقيقيّ (قضية مخالفة) ----
    // كان الاعتماد بمعرّفٍ بلا نطاق ⟹ دورٌ مقيَّد يعتمد اقتراح موظف شركةٍ أخرى.
    [Fact]
    public void AttendanceRecommendations_GuardsApproveIgnoreEditByScope()
    {
        var store = Collapse(File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Infrastructure", "Hrms", "RecommendationStore.cs")));
        // المتجر يفرض النطاق: كل معالج بتٍّ يأخذ CompanyScope ويحقنه بشرط SQL على شركة الموظف.
        Assert.Contains("ApproveAsync( ApplicationDbContext dbContext, Security.CompanyScope scope, int id)", store);
        Assert.Contains("IgnoreAsync( ApplicationDbContext dbContext, Security.CompanyScope scope, int id)", store);
        Assert.Contains("UpdateActionAsync( ApplicationDbContext dbContext, Security.CompanyScope scope,", store);
        Assert.Contains("scope.ToSqlPredicate(\"e.CompanyId\")", store);

        var page = Page("AttendanceRecommendations", "Index.cshtml.cs");
        // لم تبقَ أي مناداة بلا نطاق (النمط القديم).
        Assert.DoesNotContain("ApproveAsync(_dbContext, id)", page);
        Assert.DoesNotContain("IgnoreAsync(_dbContext, id)", page);
    }

    /// <summary>يطوي كل تتابع فراغات (بينها الأسطر) إلى مسافة واحدة — تأكيدٌ لا يتعلّق بالتنسيق.</summary>
    private static string Collapse(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ");
}
