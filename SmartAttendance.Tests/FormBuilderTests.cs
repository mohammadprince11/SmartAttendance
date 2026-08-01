using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات باني النماذج العام.
///
/// **الأهمّ** <see cref="SelectWithoutOptions_IsRejectedAtDefinition"/>: منسدلة بلا
/// خيارات تُحفظ وتُعرض فارغةً فلا يستطيع أحد الإجابة — **نموذجٌ معطَّل بصمت**،
/// وهو أخطر من خطأ ظاهر.
///
/// و<see cref="ValidateSubmission_ReturnsAllErrors_NotJustTheFirst"/>: مستخدمٌ
/// يُصحّح خطأً ليُفاجأ بآخر ثم بثالث يتخلّى عن النموذج.
/// </summary>
public class FormBuilderTests
{
    // ── أنواع الحقول ───────────────────────────────────────────────────────────

    [Fact]
    public void RatingIsOfferedToSurveysOnly()
    {
        var surveyControls = FormBuilder.ControlsFor(FormBuilder.FormTypeSurvey).Select(c => c.Key).ToList();
        var requestControls = FormBuilder.ControlsFor(FormBuilder.FormTypeRequest).Select(c => c.Key).ToList();

        Assert.Contains(FormBuilder.ControlRating, surveyControls);
        Assert.DoesNotContain(FormBuilder.ControlRating, requestControls);
        // وما عدا التقييم متاح للاثنين — الباني واحد.
        Assert.Equal(surveyControls.Count - 1, requestControls.Count);
    }

    [Fact]
    public void ExitInterviewAndTraining_CountAsSurveys()
    {
        Assert.True(FormBuilder.IsSurveyLike(FormBuilder.FormTypeExitInterview));
        Assert.True(FormBuilder.IsSurveyLike(FormBuilder.FormTypeTraining));
        Assert.False(FormBuilder.IsSurveyLike(FormBuilder.FormTypeRequest));
    }

    // ── التحقق من التعريف ──────────────────────────────────────────────────────

    [Fact]
    public void SelectWithoutOptions_IsRejectedAtDefinition()
    {
        var error = FormBuilder.ValidateField("الفرع", FormBuilder.ControlSelect, null, null);

        Assert.NotNull(error);
        Assert.Contains("خياراً واحداً", error);
    }

    [Fact]
    public void SelectWithOptions_IsAccepted()
    {
        Assert.Null(FormBuilder.ValidateField("الفرع", FormBuilder.ControlSelect, "بغداد\nالبصرة", null));
    }

    [Fact]
    public void EmptyLabelOrUnknownControl_AreRejected()
    {
        Assert.NotNull(FormBuilder.ValidateField("  ", FormBuilder.ControlText, null, null));
        Assert.NotNull(FormBuilder.ValidateField("سؤال", "NotAType", null, null));
    }

    [Fact]
    public void RatingScaleOutsideRange_IsRejected()
    {
        Assert.NotNull(FormBuilder.ValidateField("الرضا", FormBuilder.ControlRating, null, 10));
        Assert.Null(FormBuilder.ValidateField("الرضا", FormBuilder.ControlRating, null, 5));
    }

    [Fact]
    public void Options_AreDeduplicatedAndTrimmed()
    {
        var options = FormBuilder.ParseOptions(" بغداد \n البصرة\nبغداد\n\n");

        Assert.Equal(2, options.Count);
        Assert.Equal("بغداد", options[0]);
    }

    // ── سلّم التقييم ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void OddScalesAllowNeutral_EvenScalesForceAChoice(int scale, bool neutral)
    {
        Assert.Equal(neutral, FormBuilder.ScaleAllowsNeutral(scale));
    }

    [Fact]
    public void InvalidScale_FallsBackToFive()
    {
        Assert.Equal(5, FormBuilder.NormalizeScale(null));
        Assert.Equal(5, FormBuilder.NormalizeScale(11));
        Assert.Equal(3, FormBuilder.NormalizeScale(3));
    }

    // ── التحقق من الإجابة ──────────────────────────────────────────────────────

    [Fact]
    public void RequiredEmptyAnswer_IsRejected_OptionalEmptyPasses()
    {
        Assert.NotNull(FormBuilder.ValidateAnswer("السبب", FormBuilder.ControlText, true, null, "  "));
        Assert.Null(FormBuilder.ValidateAnswer("السبب", FormBuilder.ControlText, false, null, null));
    }

    [Fact]
    public void EmptyOptionalAnswer_IsNotTypeChecked()
    {
        // قيمةٌ فارغة ليست قيمةً خاطئة — وفحص نوعها يرفض نموذجاً سليماً.
        Assert.Null(FormBuilder.ValidateAnswer("المبلغ", FormBuilder.ControlNumber, false, null, ""));
    }

    [Theory]
    [InlineData(FormBuilder.ControlNumber, "abc", false)]
    [InlineData(FormBuilder.ControlNumber, "12.5", true)]
    [InlineData(FormBuilder.ControlDate, "2026-08-01", true)]
    [InlineData(FormBuilder.ControlDate, "لا تاريخ", false)]
    [InlineData(FormBuilder.ControlTime, "08:30", true)]
    public void TypedAnswers_AreChecked(string control, string answer, bool valid)
    {
        var error = FormBuilder.ValidateAnswer("حقل", control, false, null, answer);

        Assert.Equal(valid, error is null);
    }

    [Fact]
    public void YesNoQuestion_IsThreeWayNotBoolean()
    {
        Assert.Null(FormBuilder.ValidateAnswer("سؤال", FormBuilder.ControlYesNo, false, null, "بلا إجابة"));
        Assert.NotNull(FormBuilder.ValidateAnswer("سؤال", FormBuilder.ControlYesNo, false, null, "ربما"));
    }

    [Fact]
    public void AnswerOutsideDefinedOptions_IsRejected()
    {
        // إمّا تلاعب بالنموذج أو خيارٌ حُذف بعد الإجابة — وكلاهما يستحقّ الرفض.
        var error = FormBuilder.ValidateAnswer("الفرع", FormBuilder.ControlSelect, false, "بغداد\nالبصرة", "أربيل");

        Assert.NotNull(error);
        Assert.Null(FormBuilder.ValidateAnswer("الفرع", FormBuilder.ControlSelect, false, "بغداد\nالبصرة", "البصرة"));
    }

    [Fact]
    public void MultiSelect_ChecksEveryPickedValue()
    {
        var options = "بغداد\nالبصرة\nأربيل";

        Assert.Null(FormBuilder.ValidateAnswer("المدن", FormBuilder.ControlMultiSelect, false, options, "بغداد, أربيل"));
        Assert.NotNull(FormBuilder.ValidateAnswer("المدن", FormBuilder.ControlMultiSelect, false, options, "بغداد, الموصل"));
    }

    [Fact]
    public void ValidateSubmission_ReturnsAllErrors_NotJustTheFirst()
    {
        var errors = FormBuilder.ValidateSubmission(new[]
        {
            ("السبب", FormBuilder.ControlText, true, (string?)null, (string?)null),
            ("المبلغ", FormBuilder.ControlNumber, false, null, "abc"),
            ("التاريخ", FormBuilder.ControlDate, false, null, "خطأ")
        });

        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void ValidSubmission_HasNoErrors()
    {
        var errors = FormBuilder.ValidateSubmission(new[]
        {
            ("السبب", FormBuilder.ControlText, true, (string?)null, (string?)"لغرض البنك"),
            ("المبلغ", FormBuilder.ControlNumber, false, null, (string?)"250")
        });

        Assert.Empty(errors);
    }
}
