using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات قواعد طلب الوثيقة من الخدمة الذاتية.
///
/// **أهمّها** <see cref="TemplateWithoutConditions_IsAvailableToEveryone"/>: قالبٌ
/// فُتح للطلب ولم تُحدَّد له شروط مقصودٌ به الكلّ. لو عُومل كـ«نسخة سياسة» (بلا
/// شروط ⟵ لا ينطبق) لتعطّلت الميزة بصمت ولما رأى أحدٌ أي وثيقة.
///
/// و<see cref="ReviewedRequest_CannotBeReviewedAgain"/>: إعادة الاعتماد تُصدر وثيقةً
/// ثانية برقم مرجع ثانٍ، ورفضٌ بعد اعتماد يترك وثيقةً صادرةً لطلبٍ مرفوض.
/// </summary>
public class DocumentRequestPolicyTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 1);

    private static Dictionary<string, HrConditions.Fact> Facts(int branch = 5, decimal? basic = 900_000m) =>
        HrConditionFacts.Build(new HrConditionFacts.EmployeeRow
        {
            Id = 1,
            EmployeeNo = "E-001",
            BranchId = branch,
            DepartmentId = 2,
            Gender = "ذكر",
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

    // ── الأهلية ────────────────────────────────────────────────────────────────

    [Fact]
    public void TemplateWithoutConditions_IsAvailableToEveryone()
    {
        Assert.True(DocumentRequestPolicy.CanRequest(
            isActive: true, allowsEmployeeRequest: true, new HrConditions.ConditionSet(), Facts()));
    }

    [Fact]
    public void InactiveOrClosedTemplate_IsNeverRequestable()
    {
        Assert.False(DocumentRequestPolicy.CanRequest(false, true, null, Facts()));
        Assert.False(DocumentRequestPolicy.CanRequest(true, false, null, Facts()));
    }

    [Fact]
    public void ConditionsAreEnforced_SoEmployeesSeeOnlyWhatTheyDeserve()
    {
        var conditions = When("branch", HrConditions.Operator.Equal, "5");

        Assert.True(DocumentRequestPolicy.CanRequest(true, true, conditions, Facts(branch: 5)));
        Assert.False(DocumentRequestPolicy.CanRequest(true, true, conditions, Facts(branch: 9)));
    }

    [Fact]
    public void MissingFactStillBlocks_ConsistentWithTheConditionEngine()
    {
        var conditions = When("basicsalary", HrConditions.Operator.GreaterThan, "500000");

        Assert.False(DocumentRequestPolicy.CanRequest(true, true, conditions, Facts(basic: null)));
    }

    // ── التحقق ─────────────────────────────────────────────────────────────────

    [Fact]
    public void RequiredReason_IsEnforced()
    {
        Assert.NotNull(DocumentRequestPolicy.Validate(true, false, null, false));
        Assert.NotNull(DocumentRequestPolicy.Validate(true, false, "   ", false));
        Assert.Null(DocumentRequestPolicy.Validate(true, false, "لغرض البنك", false));
    }

    [Fact]
    public void RequiredAttachment_IsEnforced()
    {
        Assert.NotNull(DocumentRequestPolicy.Validate(false, true, "سبب", false));
        Assert.Null(DocumentRequestPolicy.Validate(false, true, "سبب", true));
    }

    [Fact]
    public void NothingRequired_AcceptsEmptyRequest()
    {
        Assert.Null(DocumentRequestPolicy.Validate(false, false, null, false));
    }

    [Fact]
    public void ReasonIsCheckedBeforeAttachment_SoTheFirstGapIsReported()
    {
        var error = DocumentRequestPolicy.Validate(true, true, null, false);

        Assert.Contains("سبب", error);
    }

    // ── انتقالات الحالة ────────────────────────────────────────────────────────

    [Fact]
    public void ReviewedRequest_CannotBeReviewedAgain()
    {
        Assert.True(DocumentRequestPolicy.CanReview(DocumentRequestPolicy.StatusPending));
        Assert.True(DocumentRequestPolicy.CanReview(null));
        Assert.False(DocumentRequestPolicy.CanReview(DocumentRequestPolicy.StatusApproved));
        Assert.False(DocumentRequestPolicy.CanReview(DocumentRequestPolicy.StatusRejected));
    }

    [Fact]
    public void OnlyPendingRequestsCanBeWithdrawn()
    {
        Assert.True(DocumentRequestPolicy.CanWithdraw(DocumentRequestPolicy.StatusPending));
        Assert.False(DocumentRequestPolicy.CanWithdraw(DocumentRequestPolicy.StatusApproved));
    }

    [Fact]
    public void RejectionRequiresAReason()
    {
        // «رُفض» بلا تعليل يجعل الموظف يعيد الطلب بلا فهم.
        Assert.NotNull(DocumentRequestPolicy.ValidateRejection(null));
        Assert.NotNull(DocumentRequestPolicy.ValidateRejection("  "));
        Assert.Null(DocumentRequestPolicy.ValidateRejection("البيانات غير مكتملة"));
    }

    [Fact]
    public void StatusLabels_AreDistinct()
    {
        Assert.Equal("قيد المراجعة", DocumentRequestPolicy.StatusLabel(DocumentRequestPolicy.StatusPending));
        Assert.Equal("معتمد", DocumentRequestPolicy.StatusLabel(DocumentRequestPolicy.StatusApproved));
        Assert.Equal("مرفوض", DocumentRequestPolicy.StatusLabel(DocumentRequestPolicy.StatusRejected));
    }
}
