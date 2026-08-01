using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات حسم ملف الضريبة/الضمان لكل موظف.
///
/// **الاختبار الأهمّ هنا هو اختبار عدم التغيير**: قبل هذه الطبقة كان المسير يأخذ
/// «الملف النشط» للنظام كلّه، والوعد الذي قطعتُه أن موظفاً بلا إسناد وبلا شرطٍ
/// ينطبق عليه يبقى على **نفس الملف بالضبط** — وإلا تغيّرت قسائم 1356 موظفاً
/// بمجرّد نشر الكود، وهو ما لا يُقبل على قاعدة إنتاجٍ حيّة.
/// </summary>
public class PayrollProfileResolverTests
{
    private static PayrollProfileResolver.Candidate Profile(
        int id, string name, bool active = true, int sort = 0, string? conditions = null) =>
        new(id, name, sort, active, HrConditions.Deserialize(conditions));

    /// <summary>شرط «مواطن = نعم» بصيغة تخزين المحرّك.</summary>
    private const string CitizenOnly =
        """{"groups":[{"rules":[{"criterion":"iscitizen","op":"Equal","value":"true"}]}]}""";

    private const string ExpatOnly =
        """{"groups":[{"rules":[{"criterion":"iscitizen","op":"Equal","value":"false"}]}]}""";

    private static Dictionary<string, HrConditions.Fact> Facts(bool citizen) => new()
    {
        ["iscitizen"] = HrConditions.Fact.Of(citizen)
    };

    // ── الدرجة الثالثة: سلوك اليوم محفوظ ──────────────────────────────────────

    [Fact]
    public void NoAssignmentAndNoConditions_FallsBackToActiveProfile()
    {
        var profiles = new[]
        {
            Profile(1, "قديم", active: false),
            Profile(2, "النشط"),
            Profile(3, "آخر", active: false)
        };

        var result = PayrollProfileResolver.Resolve(null, profiles, Facts(citizen: true));

        Assert.Equal(2, result.ProfileId);
        Assert.Equal(PayrollProfileResolver.Origin.Active, result.Origin);
    }

    [Fact]
    public void NoActiveProfile_FallsBackToFirstInReadOrder()
    {
        // نفس رجوع ActiveTaxProfileAsync حرفياً: أول نشط، وإلا أول ملف بالقائمة.
        var profiles = new[] { Profile(7, "الأول", active: false), Profile(8, "الثاني", active: false) };

        var result = PayrollProfileResolver.Resolve(null, profiles, Facts(citizen: true));

        Assert.Equal(7, result.ProfileId);
    }

    [Fact]
    public void NoProfilesAtAll_ResolvesToNothing()
    {
        var result = PayrollProfileResolver.Resolve(3, Array.Empty<PayrollProfileResolver.Candidate>(), Facts(true));

        Assert.Null(result.ProfileId);
        Assert.Equal(PayrollProfileResolver.Origin.None, result.Origin);
    }

    // ── الدرجة الأولى: الإسناد الصريح ─────────────────────────────────────────

    [Fact]
    public void ExplicitAssignment_BeatsActiveAndConditions()
    {
        var profiles = new[]
        {
            Profile(1, "النشط"),
            Profile(2, "المواطنون", conditions: CitizenOnly),
            Profile(3, "المُسنَد", active: false)
        };

        var result = PayrollProfileResolver.Resolve(3, profiles, Facts(citizen: true));

        Assert.Equal(3, result.ProfileId);
        Assert.Equal(PayrollProfileResolver.Origin.Assigned, result.Origin);
    }

    [Fact]
    public void AssignmentToDeletedProfile_DoesNotBreakThePayslip()
    {
        // ملفٌّ أُسند ثم حُذف: لا يجوز أن تسقط القسيمة — نُكمل للدرجات الأدنى.
        var profiles = new[] { Profile(1, "النشط") };

        var result = PayrollProfileResolver.Resolve(99, profiles, Facts(citizen: true));

        Assert.Equal(1, result.ProfileId);
        Assert.Equal(PayrollProfileResolver.Origin.Active, result.Origin);
    }

    // ── الدرجة الثانية: الشرط ─────────────────────────────────────────────────

    [Fact]
    public void CitizenCondition_SelectsCitizenProfileAutomatically()
    {
        var profiles = new[]
        {
            Profile(1, "النشط العام"),
            Profile(2, "ملف المواطنين", conditions: CitizenOnly)
        };

        var citizen = PayrollProfileResolver.Resolve(null, profiles, Facts(citizen: true));
        var expat = PayrollProfileResolver.Resolve(null, profiles, Facts(citizen: false));

        Assert.Equal(2, citizen.ProfileId);
        Assert.Equal(PayrollProfileResolver.Origin.Condition, citizen.Origin);

        // الوافد لا شرط ينطبق عليه ⟹ الملف النشط، لا ملف المواطنين.
        Assert.Equal(1, expat.ProfileId);
        Assert.Equal(PayrollProfileResolver.Origin.Active, expat.Origin);
    }

    [Fact]
    public void SortOrder_DecidesWhichConditionalProfileWins()
    {
        var profiles = new[]
        {
            Profile(1, "النشط"),
            Profile(2, "المواطنون — لاحق", sort: 5, conditions: CitizenOnly),
            Profile(3, "المواطنون — أسبق", sort: 1, conditions: CitizenOnly)
        };

        var result = PayrollProfileResolver.Resolve(null, profiles, Facts(citizen: true));

        Assert.Equal(3, result.ProfileId);
    }

    [Fact]
    public void InactiveConditionalProfile_IsNeverPicked()
    {
        var profiles = new[]
        {
            Profile(1, "النشط"),
            Profile(2, "معطّل بشرط", active: false, conditions: CitizenOnly)
        };

        var result = PayrollProfileResolver.Resolve(null, profiles, Facts(citizen: true));

        Assert.Equal(1, result.ProfileId);
    }

    [Fact]
    public void ProfileWithoutConditions_IsNeverPickedByConditionStage()
    {
        // ملفٌّ بلا شروط لا يجوز أن يُلتقط تلقائياً، وإلا صار أوّل ملفٍ بالقائمة
        // سياسةَ الشركة كلّها بصمت — نفس فخّ نُسَخ السياسة.
        var profiles = new[]
        {
            Profile(1, "بلا شروط", sort: 1),
            Profile(2, "النشط", sort: 9)
        };

        var result = PayrollProfileResolver.Resolve(null, profiles, Facts(citizen: true));

        Assert.Equal(1, result.ProfileId);
        Assert.Equal(PayrollProfileResolver.Origin.Active, result.Origin);
    }

    [Fact]
    public void MissingFact_DoesNotMatchEvenNegatedCondition()
    {
        // موظف بلا حقيقة «مواطن» مجهولٌ لا «غير مواطن» — فلا يدخل ملف الوافدين بصمت.
        var profiles = new[]
        {
            Profile(1, "النشط"),
            Profile(2, "غير المواطنين", conditions: ExpatOnly)
        };

        var result = PayrollProfileResolver.Resolve(
            null, profiles, new Dictionary<string, HrConditions.Fact>());

        Assert.Equal(1, result.ProfileId);
        Assert.Equal(PayrollProfileResolver.Origin.Active, result.Origin);
    }

    [Fact]
    public void CorruptConditionsJson_DegradesToNoConditions()
    {
        // JSON تالف يجب ألا يُسقط تشغيل المسير: يُعامَل كملفٍ بلا شروط.
        var profiles = new[]
        {
            Profile(1, "النشط"),
            Profile(2, "تالف", sort: 1, conditions: "{ليس JSON")
        };

        var result = PayrollProfileResolver.Resolve(null, profiles, Facts(citizen: true));

        Assert.Equal(1, result.ProfileId);
        Assert.Equal(PayrollProfileResolver.Origin.Active, result.Origin);
    }
}
