using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات محرّك الشروط العام.
///
/// **أهمّها على الإطلاق** <see cref="MissingFact_NeverMatches_EvenWithNegation"/>:
/// لو طابقت الحقيقةُ المفقودة عاملَ النفي، لدخل كلُّ موظف بلا راتب أساسي مسجَّل
/// (1355 من 1356 بالإنتاج) في كل نسخة سياسة شرطها «الراتب لا يساوي كذا» — بصمت
/// وبأثر مالي. الفشل الآمن هنا هو «لا تنطبق النسخة ⟵ تُطبَّق السياسة الأمّ».
///
/// وثانيها <see cref="EmptySet_MeaningIsCallersChoice"/>: «بلا شروط» معناه يختلف
/// بين نسخة سياسة (لا تنطبق) وأهلية طلب (تنطبق على الكل)، فالمحرّك يفرض على
/// المستدعي أن يقرّر صراحةً بدل افتراضٍ مخفيّ.
/// </summary>
public class HrConditionEngineTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 1);

    private static HrConditionFacts.EmployeeRow Row(
        int id = 1,
        string? gender = "ذكر",
        decimal? basic = 900_000m,
        bool citizen = true,
        DateOnly? hire = null,
        DateOnly? birth = null,
        int branch = 5) => new()
    {
        Id = id,
        EmployeeNo = $"E-{id:000}",
        BranchId = branch,
        DepartmentId = 3,
        Gender = gender,
        IsCitizen = citizen,
        IsActive = true,
        HireDate = hire ?? new DateOnly(2020, 1, 15),
        BirthDate = birth,
        BasicSalary = basic
    };

    private static Dictionary<string, HrConditions.Fact> Facts(HrConditionFacts.EmployeeRow row) =>
        HrConditionFacts.Build(row, AsOf);

    private static HrConditions.ConditionSet Set(params HrConditions.Rule[][] groups) => new()
    {
        Groups = groups.Select(rules => new HrConditions.Group { Rules = rules.ToList() }).ToList()
    };

    private static HrConditions.Rule Rule(string criterion, HrConditions.Operator op, string value) =>
        new() { Criterion = criterion, Op = op, Value = value };

    // ── القاعدة المحورية ───────────────────────────────────────────────────────

    [Fact]
    public void MissingFact_NeverMatches_EvenWithNegation()
    {
        var facts = Facts(Row(basic: null));

        var equals = Set(new[] { Rule("basicsalary", HrConditions.Operator.Equal, "500000") });
        var notEquals = Set(new[] { Rule("basicsalary", HrConditions.Operator.NotEqual, "500000") });
        var lessThan = Set(new[] { Rule("basicsalary", HrConditions.Operator.LessThan, "500000") });

        Assert.False(HrConditions.Matches(equals, facts, matchWhenEmpty: false));
        Assert.False(HrConditions.Matches(notEquals, facts, matchWhenEmpty: false));
        Assert.False(HrConditions.Matches(lessThan, facts, matchWhenEmpty: false));
    }

    [Fact]
    public void EmptySet_MeaningIsCallersChoice()
    {
        var facts = Facts(Row());
        var empty = new HrConditions.ConditionSet();

        Assert.False(HrConditions.Matches(empty, facts, matchWhenEmpty: false));
        Assert.True(HrConditions.Matches(empty, facts, matchWhenEmpty: true));
    }

    [Fact]
    public void GroupWithNoRules_IsIgnored_NotTreatedAsTrue()
    {
        var facts = Facts(Row(gender: "أنثى"));

        // مجموعة فارغة بجانب مجموعة لا تنطبق: لو عُدّت الفارغة «صح» لطابق الكل.
        var set = Set(
            new[] { Rule("gender", HrConditions.Operator.Equal, "ذكر") },
            Array.Empty<HrConditions.Rule>());

        Assert.False(HrConditions.Matches(set, facts, matchWhenEmpty: false));
    }

    // ── منطق AND/OR ────────────────────────────────────────────────────────────

    [Fact]
    public void RulesInsideGroup_AreAnded()
    {
        var facts = Facts(Row(gender: "ذكر", basic: 900_000m));

        var both = Set(new[]
        {
            Rule("gender", HrConditions.Operator.Equal, "ذكر"),
            Rule("basicsalary", HrConditions.Operator.GreaterThan, "800000")
        });

        var oneFails = Set(new[]
        {
            Rule("gender", HrConditions.Operator.Equal, "ذكر"),
            Rule("basicsalary", HrConditions.Operator.GreaterThan, "1000000")
        });

        Assert.True(HrConditions.Matches(both, facts, matchWhenEmpty: false));
        Assert.False(HrConditions.Matches(oneFails, facts, matchWhenEmpty: false));
    }

    [Fact]
    public void GroupsAreOred()
    {
        var facts = Facts(Row(gender: "أنثى", basic: 400_000m));

        var set = Set(
            new[] { Rule("gender", HrConditions.Operator.Equal, "ذكر") },
            new[] { Rule("basicsalary", HrConditions.Operator.LessThan, "500000") });

        Assert.True(HrConditions.Matches(set, facts, matchWhenEmpty: false));
    }

    // ── العوامل والأنواع ───────────────────────────────────────────────────────

    [Fact]
    public void OrderingOperators_AreRejectedOnTextAndBool()
    {
        var facts = Facts(Row(gender: "ذكر"));

        var textOrdering = Set(new[] { Rule("gender", HrConditions.Operator.GreaterThan, "ذكر") });
        var boolOrdering = Set(new[] { Rule("iscitizen", HrConditions.Operator.LessThan, "true") });

        Assert.False(HrConditions.Matches(textOrdering, facts, matchWhenEmpty: false));
        Assert.False(HrConditions.Matches(boolOrdering, facts, matchWhenEmpty: false));
    }

    [Fact]
    public void TextComparison_IgnoresCaseAndSurroundingSpace()
    {
        var facts = Facts(Row(gender: "  Male  "));
        var set = Set(new[] { Rule("gender", HrConditions.Operator.Equal, "male") });

        Assert.True(HrConditions.Matches(set, facts, matchWhenEmpty: false));
    }

    [Fact]
    public void InOperator_MatchesAnyMember_AndNotInIsItsExactComplement()
    {
        var facts = Facts(Row(branch: 5));

        var inside = Set(new[] { Rule("branch", HrConditions.Operator.In, "3,5,9") });
        var outside = Set(new[] { Rule("branch", HrConditions.Operator.NotIn, "3,5,9") });

        Assert.True(HrConditions.Matches(inside, facts, matchWhenEmpty: false));
        Assert.False(HrConditions.Matches(outside, facts, matchWhenEmpty: false));
    }

    [Fact]
    public void InOperator_WithNoParsableMembers_DoesNotMatch()
    {
        var facts = Facts(Row(branch: 5));

        // القاعدة نفسها ناقصة (قائمة بلا قيم صالحة) — النفي لا يُنجّيها.
        var inside = Set(new[] { Rule("branch", HrConditions.Operator.In, "  ") });
        var outside = Set(new[] { Rule("branch", HrConditions.Operator.NotIn, "abc") });

        Assert.False(HrConditions.Matches(inside, facts, matchWhenEmpty: false));
        Assert.False(HrConditions.Matches(outside, facts, matchWhenEmpty: false));
    }

    [Fact]
    public void BooleanCriterion_AcceptsArabicAndNumericLiterals()
    {
        var citizen = Facts(Row(citizen: true));
        var expat = Facts(Row(citizen: false));

        Assert.True(HrConditions.Matches(
            Set(new[] { Rule("iscitizen", HrConditions.Operator.Equal, "نعم") }), citizen, false));
        Assert.True(HrConditions.Matches(
            Set(new[] { Rule("iscitizen", HrConditions.Operator.Equal, "0") }), expat, false));
        Assert.False(HrConditions.Matches(
            Set(new[] { Rule("iscitizen", HrConditions.Operator.Equal, "ربما") }), citizen, false));
    }

    [Fact]
    public void UnknownCriterion_DoesNotMatch()
    {
        var facts = Facts(Row());
        var set = Set(new[] { Rule("costcenter", HrConditions.Operator.Equal, "7") });

        Assert.False(HrConditions.Matches(set, facts, matchWhenEmpty: false));
    }

    [Fact]
    public void DateCriterion_ComparesChronologically()
    {
        var facts = Facts(Row(hire: new DateOnly(2020, 1, 15)));

        Assert.True(HrConditions.Matches(
            Set(new[] { Rule("hiredate", HrConditions.Operator.LessThan, "2021-01-01") }), facts, false));
        Assert.False(HrConditions.Matches(
            Set(new[] { Rule("hiredate", HrConditions.Operator.GreaterThan, "2021-01-01") }), facts, false));
    }

    // ── الحقول الإضافية ────────────────────────────────────────────────────────

    [Fact]
    public void CustomField_BecomesAvailableCriterionAutomatically()
    {
        var row = Row() with
        {
            CustomFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["bloodtype"] = "O+"
            }
        };

        var facts = HrConditionFacts.Build(row, AsOf);
        var set = Set(new[] { Rule("custom:bloodtype", HrConditions.Operator.Equal, "o+") });

        Assert.True(HrConditions.Matches(set, facts, matchWhenEmpty: false));
        Assert.NotNull(HrConditionCatalog.Find("custom:bloodtype"));
    }

    // ── الحقائق المشتقّة ───────────────────────────────────────────────────────

    [Fact]
    public void AgeYears_CountsCompletedYearsOnly()
    {
        Assert.Equal(25, HrConditionFacts.AgeYears(new DateOnly(2001, 8, 1), AsOf));
        Assert.Equal(24, HrConditionFacts.AgeYears(new DateOnly(2001, 8, 2), AsOf));
    }

    [Fact]
    public void AgeYears_RejectsFutureBirthDate()
    {
        // خطأ إدخال شائع؛ عمرٌ سالب كان سيجعل «العمر أقل من 30» يطابق خطأً.
        Assert.Null(HrConditionFacts.AgeYears(new DateOnly(2030, 1, 1), AsOf));
    }

    [Fact]
    public void ServiceMonths_PrefersJoiningDateOverHireDate()
    {
        var hire = new DateOnly(2020, 1, 1);
        var joining = new DateOnly(2025, 8, 1);

        Assert.Equal(79, HrConditionFacts.ServiceMonths(hire, null, AsOf));
        Assert.Equal(12, HrConditionFacts.ServiceMonths(hire, joining, AsOf));
    }

    [Fact]
    public void ServiceMonths_DoesNotCountPartialMonth()
    {
        var start = new DateOnly(2026, 7, 15);

        Assert.Equal(0, HrConditionFacts.ServiceMonths(start, null, AsOf));
        Assert.Equal(1, HrConditionFacts.ServiceMonths(start, null, new DateOnly(2026, 8, 15)));
    }

    [Fact]
    public void ServiceMonths_ForFutureStart_IsMissingNotZero()
    {
        // صفرٌ كان سيطابق «مدة الخدمة أقل من 3 أشهر» لموظف لم يباشر بعد.
        Assert.Null(HrConditionFacts.ServiceMonths(new DateOnly(2027, 1, 1), null, AsOf));
    }

    // ── التخزين ────────────────────────────────────────────────────────────────

    [Fact]
    public void SerializeRoundTrip_PreservesRules()
    {
        var original = Set(
            new[] { Rule("gender", HrConditions.Operator.Equal, "ذكر") },
            new[] { Rule("basicsalary", HrConditions.Operator.GreaterOrEqual, "750000") });

        var restored = HrConditions.Deserialize(HrConditions.Serialize(original));

        Assert.Equal(2, restored.Groups.Count);
        Assert.Equal("gender", restored.Groups[0].Rules[0].Criterion);
        Assert.Equal(HrConditions.Operator.GreaterOrEqual, restored.Groups[1].Rules[0].Op);
        Assert.Equal("750000", restored.Groups[1].Rules[0].Value);
    }

    [Fact]
    public void EmptySet_SerializesToEmptyString_NotJsonNoise()
    {
        Assert.Equal(string.Empty, HrConditions.Serialize(new HrConditions.ConditionSet()));
        Assert.Equal(string.Empty, HrConditions.Serialize(null));
    }

    [Fact]
    public void CorruptJson_DegradesToEmptySet_WithoutThrowing()
    {
        var restored = HrConditions.Deserialize("{not json");

        Assert.True(restored.IsEmpty);
    }

    [Fact]
    public void Describe_RendersReadableArabicSummary()
    {
        var set = Set(
            new[]
            {
                Rule("gender", HrConditions.Operator.Equal, "ذكر"),
                Rule("servicemonths", HrConditions.Operator.GreaterThan, "12")
            },
            new[] { Rule("iscitizen", HrConditions.Operator.Equal, "نعم") });

        var text = HrConditions.Describe(set);

        Assert.Contains("الجنس يساوي ذكر", text);
        Assert.Contains(" و ", text);
        Assert.Contains(" أو ", text);
        Assert.Equal("بلا شروط", HrConditions.Describe(new HrConditions.ConditionSet()));
    }
}
