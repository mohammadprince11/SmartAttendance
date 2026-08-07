using System;
using System.IO;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حرّاس ملكية الموظف على الشاشات التي أثبت مسح العزل كشفها
/// (MULTI-TENANT-ISOLATION-SCAN.md).
///
/// <para>الجذر المعماريّ: الحارس المركزيّ كان يفحص ملكية الكيان لمسارات
/// <c>/employees/*</c> وحدها. خارجها السؤال «هل تفتح هذه الشاشة؟» لا «هل هذا الصفّ
/// لك؟» — فأربع شاشات كانت تقرأ (وتكتب) بيانات أي موظف بأي شركة.</para>
/// </summary>
public class EmployeeCompanyGuardScopeTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadPage(string relativePath) =>
        File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Pages",
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ReadSecurity(string fileName) =>
        File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Infrastructure", "Security", fileName));

    // ═══ (أ) حرّاس الشاشات ═══

    /// <summary>
    /// الشاشات التي تستهدف موظفاً مفرداً يجب أن تستدعي الحارس. غيابه هو بعينه ما
    /// أثبته المسح: صفرُ فحوصِ ملكية بأربع شاشات.
    /// </summary>
    [Theory]
    [InlineData("LeaveBalances/Adjust.cshtml.cs")]
    [InlineData("Payroll/TerminationSettlement.cshtml.cs")]
    [InlineData("Documents/View.cshtml.cs")]
    public void GuardedPages_CallTheOwnershipGuard(string page) =>
        Assert.Contains("EmployeeCompanyGuard.CanAccessEmployeeAsync", ReadPage(page));

    /// <summary>
    /// <c>LeaveBalances/Adjust</c> <b>يكتب</b>. حراسة القراءة وحدها تترك التعديل
    /// مفتوحاً — وهو الأخطر: كتابةٌ عابرة للشركات على رصيدٍ يغذّي بدل الإجازة
    /// ونهاية الخدمة. فالحارس مطلوب بالمعالجَين.
    /// </summary>
    [Fact]
    public void LeaveBalanceAdjust_GuardsReadAndWrite()
    {
        var source = ReadPage("LeaveBalances/Adjust.cshtml.cs");
        var occurrences = source.Split("EmployeeCompanyGuard.CanAccessEmployeeAsync").Length - 1;

        Assert.True(occurrences >= 2,
            $"الحارس مستدعًى {occurrences} مرّة — يجب أن يحرس OnGet **و**OnPost.");
    }

    /// <summary>
    /// سرد الوثائق حالةُ **قائمة** لا كيان مفرد: شرط <c>@EmployeeId &lt;= 0</c> كان
    /// يسرد وثائق كل الشركات. العلاج ترشيح الاستعلام لا رفض الصفحة.
    /// </summary>
    [Fact]
    public void EmployeeDocumentsList_IsFilteredByCompany()
    {
        var source = ReadPage("EmployeeDocuments/Index.cshtml.cs");

        Assert.Contains("EmployeeCompanyGuard.ListFilter", source);
        Assert.Contains("{companyFilter}", source);
    }

    /// <summary>
    /// الرفض يجب ألّا يؤكّد وجود الصفّ بشركة أخرى — وإلا صار فرقُ الاستجابات
    /// قناةَ استدلال على موظفي المنافس.
    /// </summary>
    [Fact]
    public void DocumentView_DeniesWithNotFound_NotForbid()
    {
        var source = ReadPage("Documents/View.cshtml.cs");
        var guard = source.IndexOf("EmployeeCompanyGuard.CanAccessEmployeeAsync", StringComparison.Ordinal);

        Assert.True(guard > 0);

        var after = source[guard..Math.Min(source.Length, guard + 400)];

        Assert.Contains("NotFound()", after);
        Assert.DoesNotContain("Forbid()", after);
    }

    // ═══ (ب) الحارس المركزيّ ═══

    /// <summary>
    /// المسارات التي يحمل الطلبُ هدفَها تُسجَّل مركزياً، فيُفحص الموظف بنطاق
    /// البيانات كاملاً (شركة · فرع · قسم) لا بالشركة وحدها — وتصير الحماية بحكم
    /// الموقع لا بذاكرة كاتب الصفحة.
    /// </summary>
    [Theory]
    [InlineData("/leavebalances/adjust")]
    [InlineData("/payroll/terminationsettlement")]
    [InlineData("/employeedocuments")]
    public void EmployeeTargetingRoutes_AreRegisteredCentrally(string route) =>
        Assert.Contains(route, ReadSecurity("PeopleRoutePermissionResolver.cs"));

    /// <summary>
    /// <c>/documents/view</c> <b>لا</b> يُسجَّل عمداً: معرّفه معرّف **وثيقة**،
    /// فالمحلِّل لا يستطيع استخراج الموظف من الطلب. تسجيله يجعله يفشل دائماً
    /// فيحجب شاشةً مشروعة — لذلك يُحرَس داخله. هذا الاختبار يحرس القرار نفسه.
    /// </summary>
    [Fact]
    public void DocumentViewRoute_IsDeliberatelyNotRegistered()
    {
        var resolver = ReadSecurity("PeopleRoutePermissionResolver.cs");

        Assert.DoesNotContain("\"/documents/view\"", resolver);
        Assert.Contains("EmployeeCompanyGuard.CanAccessEmployeeAsync", ReadPage("Documents/View.cshtml.cs"));
    }

    /// <summary>
    /// الحارس نفسه مغلق الفشل: معرّف غير صالح أو نطاق ممنوع ⟹ رفض بلا استعلام.
    /// (المسار المُصيب للقاعدة يحتاج SQL Server، فيُغطّى بالتكامل.)
    /// </summary>
    [Fact]
    public void Guard_RejectsInvalidInput_WithoutTouchingTheDatabase()
    {
        // لا نمرّر سياقاً حقيقياً: المسارات القصيرة تُحسم قبل أي استعلام.
        Assert.False(CompanyScope.DeniedAll().Allows(1));
        Assert.False(CompanyScope.ForCompanies(new[] { 1 }).Allows(null));
        Assert.True(CompanyScope.Unrestricted().Allows(null));
    }
}
