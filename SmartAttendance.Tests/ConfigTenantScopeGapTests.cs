using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حرّاس الفجوات التي كشفتها **المراجعة النقدية** لموجة 8D–8M (2026-08-11): العزل
/// كان يغطّي شاشات التهيئة الأربع نفسها، ويترك ثلاثة أسطح تمسّ الجداول ذاتها.
///
/// <para><b>١ — مسار كتابة بلا حارس:</b> «استبدال وأرشفة» بشاشة أنواع المناوبات
/// يؤرشف نوعاً ويرحّل خلايا روستر وإسنادات بمعرّفَين من النموذج — كان بلا تحقّق
/// نطاق، فيؤرشف مستخدمُ شركةٍ مناوبةَ شركةٍ أخرى ويعيد توجيه جداولها.</para>
///
/// <para><b>٢ — منتقيات المناوبات:</b> الروستر والإسناد والتعديلات المؤقتة ويوميات
/// اليوم كانت تسرد <c>ListAsync</c> غير المحصورة ⟹ تُقرأ أسماء مناوبات الشركات
/// الأخرى، و<c>ShiftTypeId</c> المُرسَل معها لم يكن يُتحقَّق فيُلصَق بموظفيّ.</para>
///
/// <para><b>٣ — بوابة الموظف:</b> استطلاع شركةٍ موجَّه لـ«الكل» كان يظهر لموظفي كل
/// الشركات ويقبل أصواتهم. حصرُ الشاشة الخلفية وحدها لا يكفي: الاستهلاك هنا.</para>
///
/// <para>ويحرس هذا الملف أيضاً مطابقة تعريفات الشفاء الذاتي لهجرة العمود — جدولٌ
/// يُنشئه الشفاء بعد الهجرة كان سيخرج بلا <c>CompanyId</c> فينهار كل استعلام محصور
/// بـ«Invalid column name»: كسرٌ حيّ لا ثغرة صامتة.</para>
/// </summary>
public sealed class ConfigTenantScopeGapTests
{
    [Fact]
    public async Task AreAllInScope_EmptyRequest_IsVacuouslyTrue()
    {
        // لا مرجع = لا شيء يُحرَس. (لا يلمس القاعدة، فلا سياق مطلوب.)
        Assert.True(await ConfigTenantScope.AreAllInScopeAsync(
            null!, ConfigTenantScope.ShiftTypes, Array.Empty<int>(), CompanyScope.ForCompanies(new[] { 1 })));
    }

    [Fact]
    public async Task AreAllInScope_NonPositiveId_FailsClosed()
    {
        // معرّف 0/سالب = نموذج معدَّل أو تحليل فاشل — يُرفض قبل أي استعلام.
        Assert.False(await ConfigTenantScope.AreAllInScopeAsync(
            null!, ConfigTenantScope.ShiftTypes, new[] { 5, 0 }, CompanyScope.ForCompanies(new[] { 1 })));
    }

    [Fact]
    public async Task AreAllInScope_Unrestricted_SkipsTheQuery()
    {
        // الأدمن غير المقيَّد يمرّ بلا استعلام — وإلا صار الحارس كلفةً على كل حفظ.
        Assert.True(await ConfigTenantScope.AreAllInScopeAsync(
            null!, ConfigTenantScope.ShiftTypes, new[] { 7, 9 }, CompanyScope.Unrestricted()));
    }

    [Fact]
    public void ReplaceArchive_IsGuarded_ForBothOldAndNewShift()
    {
        var page = ReadRepoFile("SmartAttendance.Web", "Pages", "ShiftTypes", "Index.cshtml.cs");

        var handler = Between(page, "OnPostReplaceArchiveAsync", "ReplaceAndArchiveAsync");
        Assert.Contains("AreAllInScopeAsync", handler);
        Assert.Contains("new[] { oldId, newId }", handler);
        Assert.Contains("NotFound()", handler);
    }

    [Theory]
    [InlineData("ShiftAssignments")]
    [InlineData("ShiftOverrides")]
    [InlineData("Roster")]
    public void ShiftPickers_AreScoped_OnTheThreeAssignmentScreens(string page)
    {
        var text = ReadRepoFile("SmartAttendance.Web", "Pages", page, "Index.cshtml.cs");

        // المنتقي معروضٌ ومُختار منه ⟹ يُحصر. (الاحتساب لا يمرّ من هنا.)
        Assert.Contains("ShiftTypeStore.ListInScopeAsync", text);
        // «await ShiftTypeStore» لا «ShiftTypeStore» وحدها: الأخيرة تطابق أيضاً
        // EmployeeShiftTypeStore.ListAsync — وهي سرد إسنادات لا منتقي مناوبات.
        Assert.DoesNotContain("await ShiftTypeStore.ListAsync(", text);
    }

    [Theory]
    [InlineData("ShiftAssignments")]
    [InlineData("ShiftOverrides")]
    [InlineData("Roster")]
    public void SubmittedShiftId_IsGuarded_NotJustHiddenFromThePicker(string page)
    {
        // إخفاء الخيار من القائمة ليس حارساً: القيمة تُرسَل بطلبٍ معدَّل.
        var text = ReadRepoFile("SmartAttendance.Web", "Pages", page, "Index.cshtml.cs");

        Assert.Contains("ConfigTenantScope.AreAllInScopeAsync", text);
        Assert.Contains("NotFound()", text);
    }

    [Fact]
    public void DayAttendance_ScopesPicker_ButKeepsTheNameDictionaryWhole()
    {
        // ⚠️ حارس عكسيّ: القاموس يسمّي مناوبات صفوفٍ **محتسبة سلفاً**؛ حصرُه يُظهر
        // «—» مكان اسم مناوبة موظفٍ يخصّني. المنتقي وحده يُحصر.
        var text = ReadRepoFile("SmartAttendance.Web", "Pages", "DayAttendance", "Index.cshtml.cs");

        Assert.Contains("_allShifts = shifts.ToDictionary(shift => shift.Id);", text);
        Assert.Contains("ShiftTypeStore.ListInScopeAsync", text);
        Assert.Contains("ShiftTypeStore.IsInScopeAsync", text);
    }

    [Fact]
    public void PortalPollFeed_IsScopedByTheEmployeesCompany()
    {
        var portal = ReadRepoFile("SmartAttendance.Web", "Pages", "EmployeePortal", "Index.cshtml.cs");

        Assert.Contains(
            "AND (p.CompanyId IS NULL\n       OR p.CompanyId = (SELECT e.CompanyId FROM Employees e WHERE e.Id = @EmployeeId))",
            portal.Replace("\r\n", "\n"));
    }

    [Fact]
    public void PortalVote_ChecksPollScope_AndThatTheOptionBelongsToThatPoll()
    {
        // خيارٌ من استطلاعٍ آخر كان يُقبَل فيُفسد نتيجتَي الاستطلاعين معاً.
        var portal = ReadRepoFile("SmartAttendance.Web", "Pages", "EmployeePortal", "Index.cshtml.cs");
        var handler = Between(portal, "OnPostVotePollAsync", "INSERT INTO EmployeePollVotes");

        Assert.Contains("INNER JOIN EmployeePollOptions o ON o.PollId = p.Id AND o.Id = @OptionId", handler);
        Assert.Contains("p.CompanyId IS NULL", handler);
        Assert.Contains("p.IsPublished = 1", handler);
    }

    [Theory]
    [InlineData("ShiftTypeStore.cs", "ShiftTypes")]
    [InlineData("ShiftRuleStore.cs", "ShiftRules")]
    [InlineData("PeriodRuleStore.cs", "PeriodRules")]
    [InlineData("EmployeeEngagementSchema.cs", "EmployeePolls")]
    public void SelfHealCreateTable_CarriesCompanyId_MatchingTheMigration(string file, string table)
    {
        // الهجرة تُضيف العمود للجدول **القائم** فقط؛ لو أنشأ الشفاء الذاتي الجدول
        // بعدها لخرج بلا العمود وانهار كل استعلام محصور. التعريفان يتطابقان.
        var text = ReadRepoFile("SmartAttendance.Web", "Infrastructure", "Hrms", file);
        var create = Between(text, $"CREATE TABLE {table}", "END;");

        Assert.Contains("CompanyId int NULL", create);
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"لم يوجد المقطع «{start}».");
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"لم يوجد «{end}» بعد «{start}».");
        return text[from..to];
    }

    private static string ReadRepoFile(params string[] parts) =>
        System.IO.File.ReadAllText(System.IO.Path.Combine(
            new[] { RepoRoot() }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
