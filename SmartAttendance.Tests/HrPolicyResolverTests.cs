using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات طبقة «سياسة أمّ + نُسَخ مشروطة مرتّبة».
///
/// **أخطر ما بهذا النمط** يحرسه <see cref="OverrideWithoutConditions_NeverWins"/>:
/// نسخة أُنشئت ونُسي ملء شروطها يجب ألا تصير هي سياسة الشركة كلّها بصمت.
///
/// و<see cref="FirstMatchingOverrideWins_BySortOrder"/> يثبّت العقد الذي تقوم عليه
/// الواجهة: الترتيب أسبقيةٌ مرئية يغيّرها المستخدم بالسحب، لا منطق تعارض خفيّ.
/// </summary>
public class HrPolicyResolverTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 1);

    private static readonly Dictionary<string, string> Parent = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Duration"] = "180",
        ["Unit"] = "Day",
        ["Basis"] = "HireDate"
    };

    private static Dictionary<string, HrConditions.Fact> FactsFor(
        string gender = "ذكر", int branch = 5, decimal? basic = 900_000m) =>
        HrConditionFacts.Build(new HrConditionFacts.EmployeeRow
        {
            Id = 1,
            EmployeeNo = "E-001",
            BranchId = branch,
            DepartmentId = 2,
            Gender = gender,
            IsCitizen = true,
            IsActive = true,
            HireDate = new DateOnly(2020, 1, 1),
            BasicSalary = basic
        }, AsOf);

    private static HrConditions.ConditionSet When(string criterion, HrConditions.Operator op, string value) => new()
    {
        Groups = new List<HrConditions.Group>
        {
            new() { Rules = new List<HrConditions.Rule> { new() { Criterion = criterion, Op = op, Value = value } } }
        }
    };

    private static HrPolicyResolver.Override Copy(
        int id,
        int sortOrder,
        HrConditions.ConditionSet conditions,
        Dictionary<string, string> payload,
        bool isActive = true) =>
        new(id, $"نسخة {id}", sortOrder, isActive, conditions, payload);

    [Fact]
    public void NoOverrides_FallsBackToParentPolicy()
    {
        var result = HrPolicyResolver.Resolve(Parent, Array.Empty<HrPolicyResolver.Override>(), FactsFor());

        Assert.Null(result.WinningOverrideId);
        Assert.Equal("سياسة الشركة", result.Source);
        Assert.Equal("180", result.Values["Duration"]);
    }

    [Fact]
    public void OverrideWithoutConditions_NeverWins()
    {
        var blank = Copy(1, 1, new HrConditions.ConditionSet(), new Dictionary<string, string> { ["Duration"] = "30" });

        var result = HrPolicyResolver.Resolve(Parent, new[] { blank }, FactsFor());

        Assert.Null(result.WinningOverrideId);
        Assert.Equal("180", result.Values["Duration"]);
    }

    [Fact]
    public void FirstMatchingOverrideWins_BySortOrder()
    {
        var facts = FactsFor(branch: 5);

        var second = Copy(2, 2, When("branch", HrConditions.Operator.Equal, "5"),
            new Dictionary<string, string> { ["Duration"] = "60" });
        var first = Copy(1, 1, When("branch", HrConditions.Operator.Equal, "5"),
            new Dictionary<string, string> { ["Duration"] = "90" });

        // تُمرَّر بترتيب مقلوب عمداً: الحسم يعتمد SortOrder لا ترتيب المجموعة.
        var result = HrPolicyResolver.Resolve(Parent, new[] { second, first }, facts);

        Assert.Equal(1, result.WinningOverrideId);
        Assert.Equal("90", result.Values["Duration"]);
    }

    [Fact]
    public void InactiveOverride_IsSkipped_AndNextOneWins()
    {
        var facts = FactsFor(branch: 5);

        var disabled = Copy(1, 1, When("branch", HrConditions.Operator.Equal, "5"),
            new Dictionary<string, string> { ["Duration"] = "90" }, isActive: false);
        var enabled = Copy(2, 2, When("branch", HrConditions.Operator.Equal, "5"),
            new Dictionary<string, string> { ["Duration"] = "60" });

        var result = HrPolicyResolver.Resolve(Parent, new[] { disabled, enabled }, facts);

        Assert.Equal(2, result.WinningOverrideId);
        Assert.Equal("60", result.Values["Duration"]);
    }

    [Fact]
    public void PartialPayload_MergesOverParent_KeepingUntouchedKeys()
    {
        var facts = FactsFor(branch: 5);

        var copy = Copy(1, 1, When("branch", HrConditions.Operator.Equal, "5"),
            new Dictionary<string, string> { ["Duration"] = "60" });

        var result = HrPolicyResolver.Resolve(Parent, new[] { copy }, facts);

        Assert.Equal("60", result.Values["Duration"]);
        // لم تذكرهما النسخة ⟹ يبقيان من الأمّ، فتعديل الأمّ يسري على النُسَخ.
        Assert.Equal("Day", result.Values["Unit"]);
        Assert.Equal("HireDate", result.Values["Basis"]);
    }

    [Fact]
    public void NonMatchingOverride_LeavesParentIntact()
    {
        var facts = FactsFor(branch: 9);

        var copy = Copy(1, 1, When("branch", HrConditions.Operator.Equal, "5"),
            new Dictionary<string, string> { ["Duration"] = "60" });

        var result = HrPolicyResolver.Resolve(Parent, new[] { copy }, facts);

        Assert.Null(result.WinningOverrideId);
        Assert.Equal("180", result.Values["Duration"]);
    }

    [Fact]
    public void Resolve_DoesNotMutateParentDictionary()
    {
        var facts = FactsFor(branch: 5);
        var copy = Copy(1, 1, When("branch", HrConditions.Operator.Equal, "5"),
            new Dictionary<string, string> { ["Duration"] = "60" });

        HrPolicyResolver.Resolve(Parent, new[] { copy }, facts);

        // الأمّ قد تكون مشتركة بين موظفين بحلقة احتساب — تلويثها يسرّب نسخة موظف لآخر.
        Assert.Equal("180", Parent["Duration"]);
    }

    // ── إعادة الترتيب بالسحب ───────────────────────────────────────────────────

    [Fact]
    public void Reorder_AssignsSequentialRanksInGivenOrder()
    {
        var existing = new[]
        {
            Copy(1, 1, new HrConditions.ConditionSet(), new Dictionary<string, string>()),
            Copy(2, 2, new HrConditions.ConditionSet(), new Dictionary<string, string>()),
            Copy(3, 3, new HrConditions.ConditionSet(), new Dictionary<string, string>())
        };

        var ranks = HrPolicyResolver.Reorder(new[] { 3, 1, 2 }, existing);

        Assert.Equal(1, ranks[3]);
        Assert.Equal(2, ranks[1]);
        Assert.Equal(3, ranks[2]);
    }

    [Fact]
    public void Reorder_KeepsRowsMissingFromTheDraggedList()
    {
        var existing = new[]
        {
            Copy(1, 1, new HrConditions.ConditionSet(), new Dictionary<string, string>()),
            Copy(2, 2, new HrConditions.ConditionSet(), new Dictionary<string, string>()),
            Copy(9, 9, new HrConditions.ConditionSet(), new Dictionary<string, string>())
        };

        // 9 أُضيفت بعد تحميل الصفحة ولم ترسلها الواجهة — يجب ألا تختفي أسبقيتها.
        var ranks = HrPolicyResolver.Reorder(new[] { 2, 1 }, existing);

        Assert.Equal(1, ranks[2]);
        Assert.Equal(2, ranks[1]);
        Assert.Equal(3, ranks[9]);
    }

    // ── الحمولة ────────────────────────────────────────────────────────────────

    [Fact]
    public void PayloadRoundTrip_IsCaseInsensitiveOnKeys()
    {
        var json = HrPolicyResolver.SerializePayload(new Dictionary<string, string> { ["Duration"] = "60" });
        var back = HrPolicyResolver.DeserializePayload(json);

        Assert.Equal("60", back["duration"]);
    }

    [Fact]
    public void CorruptPayload_DegradesToEmpty_SoCopyEqualsParent()
    {
        Assert.Empty(HrPolicyResolver.DeserializePayload("}{"));
        Assert.Empty(HrPolicyResolver.DeserializePayload(null));
    }

    [Fact]
    public void ChangedKeys_ListsOnlyRealDifferences()
    {
        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Duration"] = "60",
            ["Unit"] = "Day",
            ["Extra"] = "1"
        };

        var changed = HrPolicyResolver.ChangedKeys(Parent, payload);

        Assert.Contains("Duration", changed);
        Assert.Contains("Extra", changed);
        Assert.DoesNotContain("Unit", changed);
    }
}
