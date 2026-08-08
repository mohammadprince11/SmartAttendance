using System;
using System.IO;
using System.Text.RegularExpressions;
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

    private static string ReadHrms(string fileName) =>
        File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Infrastructure", "Hrms", fileName));

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
    ///
    /// <para><b>لماذا لا نقيس مسافةً نصّيّة بعد نداء الحارس؟</b> النسخة الأولى قرأت
    /// ٤٠٠ محرف بعد أول <c>CanAccessEmployeeAsync</c> وبحثت فيها عن <c>NotFound()</c>.
    /// حين جُمِعت الحراسة ببوّابة واحدة (<c>CanAccessDocumentAsync</c>) صار النداء
    /// داخل البوّابة و<c>NotFound()</c> عند مستدعيها، فسقط الاختبار مع أن الصفحة
    /// صارت <i>أحكم</i>: أربعة معالِجات محروسة بدل واحد. مقياسُ قربٍ نصّيّ يعاقب
    /// إعادةَ التنظيم بدل أن يحرس الخاصيّة — والخاصيّة هي: لا <c>Forbid</c> بالصفحة
    /// إطلاقاً، وكل بوّابةٍ تُرفض بـ<c>NotFound</c>.</para>
    /// </summary>
    [Fact]
    public void DocumentView_DeniesWithNotFound_NotForbid()
    {
        var source = ReadPage("Documents/View.cshtml.cs");

        Assert.Contains("EmployeeCompanyGuard.CanAccessEmployeeAsync", source);

        // لا استجابة تفرّق «ممنوع» عن «غير موجود» بهذه الصفحة — وإلا عاد التسريب.
        Assert.DoesNotContain("Forbid()", source);

        // وكل بوّابة تُرفض بـNotFound: مسارُ رفضٍ منسيّ يعيد فتح القناة.
        var denials = Regex.Matches(source, @"if \(!await CanAccess\w+Async\([^)]*\)\)");

        Assert.NotEmpty(denials);

        foreach (Match denial in denials)
        {
            var tail = source[denial.Index..Math.Min(source.Length, denial.Index + 240)];

            Assert.True(tail.Contains("NotFound()", StringComparison.Ordinal),
                $"بوّابة عند المحرف {denial.Index} لا تُرفض بـNotFound: «{denial.Value}».");
        }
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

    // ═══ تغطية المعالجات — الدرس الذي كلّف ثغرة ═══

    /// <summary>
    /// <b>الاختبار الذي كان ناقصاً.</b> الفحص السابق كان
    /// <c>Assert.Contains("EmployeeCompanyGuard…")</c> — يمرّ بحارسٍ واحد من أربعة.
    /// وهذا بالضبط ما وقع: <c>Documents/View</c> فيه أربعة معالجات
    /// (<c>OnGetAsync</c> · <c>OnPostRevokeAsync</c> · <c>OnPostFileAsync</c> ·
    /// <c>OnGetStampAsync</c>) وحُرس أوّلها وحده، فبقي الإبطال والإيداع وخدمة ملف
    /// الختم مفتوحةً بمعرّفٍ من الطلب.
    ///
    /// <para>الدرس: اختبارُ <b>وجودٍ</b> يُطمئن زوراً؛ المطلوب اختبارُ <b>تغطية</b>.
    /// هذا الاختبار يعدّ المعالجات ويعدّ البوابات ويوجب تساويهما.</para>
    /// </summary>
    [Theory]
    [InlineData("Documents/View.cshtml.cs", "CanAccessDocumentAsync(id)", 3)]
    public void EveryHandlerOnGuardedPage_IsCovered(string page, string gateCall, int expectedGuardedHandlers)
    {
        var source = ReadPage(page);
        var gates = source.Split(gateCall).Length - 1;

        Assert.True(gates >= expectedGuardedHandlers,
            $"«{page}»: بوابات = {gates}، والمطلوب {expectedGuardedHandlers} على الأقل. "
            + "معالجٌ بلا بوابة يعني معرّفاً من الطلب يعمل بلا فحص ملكية.");
    }

    /// <summary>عددُ المعالجات التي تأخذ معرّفاً يجب ألّا يتجاوز عدد البوابات.</summary>
    [Fact]
    public void DocumentView_HasNoHandlerWithoutAGate()
    {
        var source = ReadPage("Documents/View.cshtml.cs");

        var handlersTakingId = Regex.Matches(source, @"public async Task<IActionResult> On\w+Async\(int id\)").Count;
        var gates = source.Split("CanAccessDocumentAsync(id)").Length - 1;

        Assert.Equal(handlersTakingId, gates);
    }

    // ═══ بوابة الصفّ المملوك ═══

    /// <summary>
    /// أسماء الجداول تُحقن نصّاً بالاستعلام، فالحارس يرفض أي معرّف غير صالح —
    /// الخطأ البرمجيّ يُرفع استثناءً لا يُترجم استعلاماً.
    /// </summary>
    [Theory]
    [InlineData("Employees; DROP TABLE X")]
    [InlineData("Employees WHERE 1=1")]
    [InlineData("")]
    [InlineData("1Table")]
    [InlineData("Employees'")]
    public void RowGuard_RejectsAnythingButAPlainIdentifier(string identifier) =>
        Assert.Throws<ArgumentException>(() => EmployeeCompanyGuard.GuardIdentifier(identifier));

    [Theory]
    [InlineData("EmployeeLoans")]
    [InlineData("SelfServiceRequests")]
    [InlineData("_Staging1")]
    public void RowGuard_AcceptsValidIdentifiers(string identifier) =>
        EmployeeCompanyGuard.GuardIdentifier(identifier);

    /// <summary>الأسماء المستعملة فعلاً بالكود يجب أن تجتاز الحارس.</summary>
    [Fact]
    public void DeclaredTableNames_AreValidIdentifiers()
    {
        EmployeeCompanyGuard.GuardIdentifier(EmployeeCompanyGuard.Tables.EmployeeLoans);
        EmployeeCompanyGuard.GuardIdentifier(EmployeeCompanyGuard.Tables.SelfServiceRequests);
    }

    /// <summary>
    /// الكتابات المالية بمعرّفٍ من النموذج تمرّ ببوابة الصفّ — لكن <b>بطبقة المتجر</b>
    /// لا الصفحة (موجة التحصين 2026-08-08): جعل النطاق وسيطاً إلزامياً بتوقيع المتجر
    /// يجعل المترجم يفرض الفحص على كل مستدعٍ، فلا يعتمد على تذكّر كاتب الصفحة.
    /// <see cref="StoresEnforceOwnershipGuard"/> يحرس ذلك؛ وهنا نبقي على ما تبقّى
    /// بالصفحة (حذف الطلب المالي محروسٌ بالصفحة أصلاً).
    /// </summary>
    [Fact]
    public void FinancialRequestsPage_DeleteIsGated() =>
        Assert.Contains("EmployeeCompanyGuard.CanAccessOwnedRowAsync",
            ReadPage("Payroll/FinancialRequests.cshtml.cs"));

    /// <summary>
    /// موجة التحصين (2026-08-08): الكتابات المالية بمعرّفٍ من النموذج (اعتماد/رفض/
    /// حذف/قفل/ترحيل قسط) عُزلت بطبقة المتجر — كل متجرٍ منها يستدعي بوابة الصفّ
    /// المملوك، فالمترجم يفرض تمرير النطاق ولا يُنسى فحصٌ عند مستدعٍ جديد.
    /// </summary>
    [Theory]
    [InlineData("LoanStore.cs")]                 // ترحيل الأقساط + الاعتماد/الحذف/الجدول
    [InlineData("ContractRegisterStore.cs")]     // قفل/حذف حركة العقد
    [InlineData("ApprovalWorkflowEngine.cs")]    // اعتماد/رفض الطلب المالي المشترك
    public void StoresEnforceOwnershipGuard(string store) =>
        Assert.Contains("EmployeeCompanyGuard.CanAccessOwnedRowAsync", ReadHrms(store));

    /// <summary>
    /// ترحيل أقساط القروض كان يعبر كل الشركات (P0): يجب أن يحصر بالنطاق عبر
    /// <see cref="EmployeeCompanyGuard.ListFilter"/>، وأن يكون idempotent بقفلٍ
    /// محدِّث داخل معاملة كي لا تُرحّل ضغطتان متزامنتان نفس القسط مرتين.
    /// </summary>
    [Fact]
    public void PostDueInstallments_IsScopedAndIdempotent()
    {
        var store = ReadHrms("LoanStore.cs");

        Assert.Contains("EmployeeCompanyGuard.ListFilter", store);
        Assert.Contains("UPDLOCK, HOLDLOCK", store);
        Assert.Contains("BeginTransactionAsync", store);
    }

    /// <summary>
    /// لوحة الحضور تجمّع بالـSQL لا بشحن يوميات المدى للذاكرة (الموجة 6 — P1):
    /// الشاشة تنادي التجميعة، والمتجر يحسب البائتة مجموعياً لا بـEXISTS لكل صفّ.
    /// </summary>
    [Fact]
    public void AttendanceDashboard_AggregatesInSql()
    {
        var page = ReadPage("AttendanceDashboard/Index.cshtml.cs");
        Assert.Contains("DashboardAggregateAsync", page);
        Assert.DoesNotContain("ListRangeAsync", page);

        // البائتة مجموعياً بوصلة مسبقة التجميع (نفس نمط الترقيم) لا مترابطة لكل صفّ.
        var store = ReadHrms("DayAttendanceStore.cs");
        Assert.Contains("DashboardAggregateAsync", store);
    }

}
