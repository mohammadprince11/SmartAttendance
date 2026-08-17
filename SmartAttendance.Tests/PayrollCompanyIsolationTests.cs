using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// عزل شركات المسير — العطل الحرج الذي كشفه تدقيق الجاهزية (2026-08-07).
///
/// <para><b>ما كان</b>: `PayrollRuns` بلا عمود شركة، والاحتساب يختار الموظفين بـ
/// <c>WHERE IsDeleted=0 AND IsActive=1</c> بلا ترشيح ⟹ دفعة شركة تولّد قسائم
/// لموظفي شركةٍ أخرى. وإحدى عشرة نقطة تأخذ <c>runId</c> من الطلب بلا فحص ملكية —
/// منها إرسال القسائم بالبريد، وحذف الدفعة، وتصدير ملف البنك بالآيبانات.</para>
///
/// <para>هذه الاختبارات تحرس القرار بمستويين: منطق النطاق نفسه (نقيّ)، وحارس نصّيّ
/// يمنع عودة نقطةٍ بلا بوابة.</para>
/// </summary>
public class PayrollCompanyIsolationTests
{
    // ═══ منطق النطاق ═══

    [Fact]
    public void Unrestricted_SeesEverything_IncludingUnattributedRows()
    {
        var scope = CompanyScope.Unrestricted();

        Assert.True(scope.Allows(1));
        Assert.True(scope.Allows(999));
        Assert.True(scope.Allows(null));
        Assert.False(scope.IsDeniedAll);
    }

    [Fact]
    public void ScopedUser_SeesOwnCompanyOnly()
    {
        var scope = CompanyScope.ForCompanies(new[] { 1 });

        Assert.True(scope.Allows(1));
        Assert.False(scope.Allows(2));
    }

    /// <summary>
    /// الصفّ بلا شركة (تاريخيّ أو مختلط تعذّرت نسبته) يراه غير المقيَّد وحده.
    /// قبوله للمقيَّد يفتح الثغرة من الباب الخلفي؛ ورفضه للجميع يُخفي بيانات
    /// تاريخية عن مالكها — فالوسط أن تبقى مرئية للأدمن حتى تُنسَب.
    /// </summary>
    [Fact]
    public void UnattributedRow_IsHiddenFromScopedUsers()
    {
        Assert.False(CompanyScope.ForCompanies(new[] { 1 }).Allows(null));
        Assert.False(CompanyScope.ForCompanies(new[] { 1 }).Allows(0));
    }

    [Fact]
    public void DeniedAll_AllowsNothing()
    {
        var scope = CompanyScope.DeniedAll();

        Assert.True(scope.IsDeniedAll);
        Assert.False(scope.Allows(1));
        Assert.False(scope.Allows(null));
    }

    /// <summary>القيم غير الصالحة تُسقَط بالمُنشئ فلا تتسرّب لشرط الـSQL.</summary>
    [Fact]
    public void NonPositiveCompanyIds_AreDropped()
    {
        var scope = CompanyScope.ForCompanies(new[] { 0, -5, 3 });

        Assert.Equal(new[] { 3 }, scope.AllowedCompanyIds);
        Assert.False(scope.Allows(0));
        Assert.False(scope.Allows(-5));
    }

    // ═══ شرط الـSQL ═══

    [Fact]
    public void SqlPredicate_IsNeutralForUnrestricted() =>
        Assert.Equal("1 = 1", CompanyScope.Unrestricted().ToSqlPredicate("r.CompanyId"));

    [Fact]
    public void SqlPredicate_IsImpossibleForDeniedAll() =>
        Assert.Equal("1 = 0", CompanyScope.DeniedAll().ToSqlPredicate("r.CompanyId"));

    [Fact]
    public void SqlPredicate_ListsAllowedCompaniesOnly() =>
        Assert.Equal(
            "r.CompanyId IN (2, 7)",
            CompanyScope.ForCompanies(new[] { 7, 2, 7 }).ToSqlPredicate("r.CompanyId"));

    /// <summary>
    /// الشرط يُحقن نصّاً بالاستعلام، فيجب أن يبقى أرقاماً بحتة. المُنشئ يقبل
    /// <c>int</c> فقط، والحارس هنا يثبت أن الناتج لا يحمل أي محرف غير رقميّ.
    /// </summary>
    [Fact]
    public void SqlPredicate_ContainsDigitsOnly_NoInjectionSurface()
    {
        var predicate = CompanyScope.ForCompanies(new[] { 11, 22 }).ToSqlPredicate("r.CompanyId");
        var inList = predicate[(predicate.IndexOf('(') + 1)..predicate.IndexOf(')')];

        Assert.Matches(@"^[0-9, ]+$", inList);
    }

    [Fact]
    public void SqlPredicate_RejectsEmptyColumn() =>
        Assert.Throws<ArgumentException>(
            () => CompanyScope.ForCompanies(new[] { 1 }).ToSqlPredicate("  "));

    // ═══ الحارس النصّيّ: لا نقطة بلا بوابة ═══

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
            RepoRoot(), "SmartAttendance.Web", "Pages", "Payroll",
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// كل معالج بـ`Runs` يعمل على دفعة يمرّ من `ActAsync(id, …)` — والتوقيع يستقبل
    /// **مصنعاً** لا مهمّةً بدأت، فلا تنطلق العملية قبل الفحص.
    /// </summary>
    [Theory]
    [InlineData("OnPostSetScopeAsync")]
    [InlineData("OnPostPreflightAsync")]
    [InlineData("OnPostCalculateAsync")]
    [InlineData("OnPostApproveAsync")]
    [InlineData("OnPostLockAsync")]
    [InlineData("OnPostIssueAsync")]
    [InlineData("OnPostSendAsync")]
    [InlineData("OnPostReopenAsync")]
    [InlineData("OnPostDeleteAsync")]
    public void EveryRunHandler_GoesThroughTheGate(string handler)
    {
        var source = ReadPage("Runs.cshtml.cs");
        var index = source.IndexOf(handler, StringComparison.Ordinal);

        Assert.True(index > 0, $"المعالج «{handler}» غير موجود — حُذف أو أُعيدت تسميته.");

        // جسم المعالج حتى نهاية السطر أو الكتلة القصيرة يجب أن يستدعي البوابة.
        var body = source[index..Math.Min(source.Length, index + 400)];

        Assert.Contains("ActAsync(id,", body);
    }

    /// <summary>
    /// التوقيع القديم (`ActAsync(Task)`) كان يستقبل مهمّة **بدأت فعلاً**، فأي حارس
    /// بداخله يقع بعد تنفيذ العملية. عودته تُعيد الثغرة صامتةً.
    /// </summary>
    [Fact]
    public void ActAsync_TakesAFactory_NotAStartedTask()
    {
        var source = ReadPage("Runs.cshtml.cs");

        Assert.Contains("Func<Task<(bool Ok, string Message)>> action", source);
        Assert.DoesNotMatch(
            new Regex(@"ActAsync\(\s*Task<\(bool"),
            source);
    }

    /// <summary>البوابة نفسها تُستدعى داخل `ActAsync` قبل تشغيل العملية.</summary>
    [Fact]
    public void TheGate_IsCalledBeforeTheAction()
    {
        var source = ReadPage("Runs.cshtml.cs");
        var gate = source.IndexOf("CanAccessRunAsync", StringComparison.Ordinal);
        var invoke = source.IndexOf("await action()", StringComparison.Ordinal);

        Assert.True(gate > 0, "البوابة غير مستدعاة بـRuns.");
        Assert.True(invoke > 0, "تشغيل العملية غير موجود.");
        Assert.True(gate < invoke, "البوابة تقع بعد تشغيل العملية — بلا أثر.");
    }

    /// <summary>
    /// `RunDetail` يعرض القسائم **ويُصدِّر ملف البنك بالآيبانات**؛ كلا معالجيه
    /// يجب أن يبدأ بالفحص.
    /// </summary>
    [Theory]
    [InlineData("OnGetAsync")]
    [InlineData("OnGetBankFileAsync")]
    public void EveryRunDetailHandler_ChecksAccessFirst(string handler)
    {
        var source = ReadPage("RunDetail.cshtml.cs");
        var index = source.IndexOf(handler, StringComparison.Ordinal);

        Assert.True(index > 0, $"المعالج «{handler}» غير موجود.");

        var body = source[index..Math.Min(source.Length, index + 300)];

        Assert.Contains("CanAccessAsync()", body);
    }

    private static string ReadStore(string file) =>
        File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Infrastructure", "Hrms", file));

    /// <summary>
    /// 🛡️ عزل الشركات بقفل الحركات (Issue 6): كان `LockForRunAsync` يقفل حركات كل
    /// الشركات للشهر (WHERE Year+Month فقط). الحارس النصّي يفشل إن عاد القفل بلا
    /// حصرٍ بموظفي الدفعة: يجب أن يربط الحركة بالموظف وبشركة الدفعة وأعضاء نطاقها.
    /// </summary>
    [Fact]
    public void LockForRun_IsScopedToRunEmployees_NotWholeMonth()
    {
        var source = ReadStore("PayrollTransactionStore.cs");
        var start = source.IndexOf("LockForRunAsync", StringComparison.Ordinal);
        Assert.True(start > 0, "LockForRunAsync غير موجود — حُذف أو أُعيدت تسميته.");

        // جسم الدالة حتى القوس المغلق التالي تقريباً.
        var body = source[start..Math.Min(source.Length, start + 1400)];

        Assert.Contains("INNER JOIN Employees", body);
        Assert.Contains("PayrollRuns r", body);
        Assert.Contains("e.CompanyId = r.CompanyId", body);
        Assert.Contains("PayrollRunScopeMembers", body);
    }
}
