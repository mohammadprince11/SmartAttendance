using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// بوّابة عزل الشركات (Phases 22/23) — **سقّاطة** لا شهادة نظافة.
///
/// <para>كل ثغرات عبور الشركات بهذا المستودع (الستّ P0 بموجة 2026-08-11، والثلاث
/// التي كشفتها مراجعة 8D–8M بعدها) وُلدت من النمط ذاته: صفحةٌ تقبل معرّفاً من
/// المتصفّح وتكتب به بلا فحص ملكية. الاختبارات النصّية السابقة تحرس الصفحات
/// المُصلَحة بالاسم — فلا تمنع **صفحةً جديدة** تكرّر النمط غداً. هذا الملف يقلبها:
/// يمسح كل نماذج الصفحات ويفشل على أي صفحة تقبل معرّفاً بلا حارس، إلا ما هو
/// مُدرَجٌ صراحةً بخط الأساس أدناه.</para>
///
/// <para><b>السقّاطة تشدّ ولا ترخي:</b> الاختبار الثاني يفشل على أي مُدخَل بخط
/// الأساس صار محروساً أو حُذف — فلا يبقى الدَّين مذكوراً بعد سداده، ولا يتحوّل خط
/// الأساس إلى مقبرةٍ تُخبّئ صفحاتٍ لم تعد موجودة.</para>
///
/// <para>⚠️ <b>خط الأساس دَينٌ لا براءة.</b> كونه هنا يعني «لم تُدقَّق بعد»، لا
/// «آمنة». أغلبه تهيئة عالمية يحصرها كتالوج الأدوار بالأدمن، لكن ذلك **لم يُتحقَّق
/// صفحةً صفحة** — وهذا هو مسح Phase 21 المتبقّي.</para>
/// </summary>
public sealed class TenantGuardRatchetTests
{
    /// <summary>
    /// قراءة **معرّفٍ** من الطلب: وسيط <c>int</c> بمعالِج POST، أو حقل نموذج اسمه
    /// معرّف. مجرّد وجود <c>OnPost</c> لا يكفي — صفحات الإنشاء والاستيراد تكتب بلا
    /// أن تستهدف صفّاً قائماً، فليست من هذا الصنف.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex TakesIdentifier = new(
        """OnPost\w*\(\s*int\s|Request\.Form\["\w*[Ii]d\w*"\]""",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>أي ذكرٍ لأحد هذه يعني أن الصفحة تفرض النطاق بنفسها.</summary>
    private static readonly string[] GuardTokens =
    {
        "CompanyScope", "EmployeeCompanyGuard", "ConfigTenantScope", "EffectiveScope",
        // إعداد عالمي لا يملك CompanyId: حارس الأدمن الصريح يعادل نطاقاً غير مقيّد.
        "IsAdministrator()", "IsAdmin"
    };

    /// <summary>
    /// مسارات يفرض عليها <c>RoleSecurityMiddleware</c> ملكية الكيان مركزيّاً
    /// (عبر <c>PeopleRoutePermissionResolver</c>)، أو تُحصر بالموظف صاحب الجلسة —
    /// فالحارس موجودٌ خارج الصفحة، لا داخلها.
    /// </summary>
    private static readonly string[] RouteGuardedPrefixes =
    {
        "Employees/", "EmployeePermissions/", "EmployeePortal/", "MyProfile/"
    };

    /// <summary>
    /// **دَين موروث** (2026-08-11): صفحات تقبل معرّفاً بلا حارس داخلها. تُسدَّد
    /// بمسح Phase 21؛ وكل مُدخَل يُسدَّد **يجب** أن يُحذف من هنا وإلا فشل الاختبار.
    /// </summary>
    private static readonly string[] Baseline =
    {
        "CompanyDocuments/Index.cshtml.cs",
        "Documents/Generate.cshtml.cs",
        "Documents/Requests.cshtml.cs",
        "Documents/Templates.cshtml.cs",
        "Engagement/Announcements.cshtml.cs",
        "Engagement/Feedback.cshtml.cs",
        "Forms/Index.cshtml.cs",
        "Forms/Submissions.cshtml.cs",
        "HrSettings/EmployeeGroups.cshtml.cs",
        "HrSettings/EntityFields.cshtml.cs",
        "HrSettings/Formulas.cshtml.cs",
        "HrSettings/Lookups.cshtml.cs",
        "HrSettings/NotificationCenter.cshtml.cs",
        "HrSettings/ProbationPeriod.cshtml.cs",
        "HrSettings/RequestTypes.cshtml.cs",
        "HrSettings/TerminationReasons.cshtml.cs",
        "HrSettings/ViolationConfiguration.cshtml.cs",
        // LeaveRequests/Delete.cshtml.cs — سُدِّد (AUTHZ-003): النطاق يُفرض داخل
        // LeaveRequestService لا بالصفحة. أُزيل من خط الأساس فلا يعود دَيناً مقبولاً.
        "Notifications/Bell.cshtml.cs",
        "PositionCategories/Index.cshtml.cs",
        "PositionLevels/Index.cshtml.cs",
        "Positions/Index.cshtml.cs",
        // Violations/Index.cshtml.cs — سُدِّد (AUTHZ-006 · FIX-001): فلتر شركةٍ على
        // القوائم الثلاث. أُزيل من خط الأساس فلا يعود دَيناً مقبولاً.
    };

    [Fact]
    public void NoNewPage_AcceptsAnIdentifier_WithoutAScopeGuard()
    {
        var offenders = UnguardedPages().Except(Baseline, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(offenders.Count == 0,
            "صفحة تقبل معرّفاً من الطلب بلا حارس نطاق — وهذا النمط ذاته أنتج كل ثغرات " +
            "عبور الشركات السابقة. افرض النطاق داخل الصفحة (ConfigTenantScope أو " +
            "EmployeeCompanyGuard) أو برّر الاستثناء صراحةً بخط الأساس:\n  " +
            string.Join("\n  ", offenders));
    }

    [Fact]
    public void Baseline_HasNoStaleEntry_SoTheRatchetOnlyTightens()
    {
        var unguarded = UnguardedPages().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stale = Baseline.Where(entry => !unguarded.Contains(entry)).ToList();

        Assert.True(stale.Count == 0,
            "مُدخَلات بخط الأساس لم تعد بلا حارس (أو حُذفت صفحتها) — احذفها منه كي " +
            "يبقى الدَّين المذكور هو الدَّين الحقيقيّ:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void TheScanner_ActuallySeesTheRepository()
    {
        // حارس الحارس: خطأ مسارٍ يجعل المسح يعود فارغاً فيمرّ كل شيء «أخضر» كذباً.
        Assert.True(PageModels().Count > 100);
        Assert.Contains(PageModels(), p => Relative(p) == "ShiftTypes/Index.cshtml.cs");
    }

    private static IEnumerable<string> UnguardedPages()
    {
        foreach (var path in PageModels())
        {
            var relative = Relative(path);

            if (RouteGuardedPrefixes.Any(prefix => relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var text = File.ReadAllText(path);

            if (!TakesIdentifier.IsMatch(text))
            {
                continue;
            }

            if (GuardTokens.Any(token => text.Contains(token, StringComparison.Ordinal)))
            {
                continue;
            }

            yield return relative;
        }
    }

    private static List<string> PageModels() =>
        Directory.GetFiles(PagesRoot(), "*.cshtml.cs", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal).ToList();

    private static string Relative(string path) =>
        Path.GetRelativePath(PagesRoot(), path).Replace('\\', '/');

    private static string PagesRoot() =>
        Path.Combine(RepoRoot(), "SmartAttendance.Web", "Pages");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
