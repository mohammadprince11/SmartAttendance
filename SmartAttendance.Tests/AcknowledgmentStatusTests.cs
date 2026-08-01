using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات حالة الإقرار.
///
/// القيمة كلها بالإثبات، و**فصل المشاهدة عن الموافقة** هو جوهر ذلك: «أُرسل ولم
/// يُشاهَد» موقفٌ قانوني مستقل تماماً عن «شوهد ولم يُوافَق عليه»، ودمجهما بحالة
/// واحدة يمحو أهمّ ما تحتاجه الشركة بنزاع عمّالي.
/// </summary>
public class AcknowledgmentStatusTests
{
    private static readonly DateTime Sent = new(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);

    private static AcknowledgmentStore.Assignment Row(
        DateTime? viewed = null,
        DateTime? accepted = null,
        DateTime? declined = null) =>
        new(1, 10, "سياسة أخلاقيات العمل", 5, "موظف", "E-005",
            Sent, viewed, accepted, declined, "عنوان", "نصّ");

    [Fact]
    public void SentButNeverOpened_IsItsOwnStatus()
    {
        var row = Row();

        Assert.False(row.Viewed);
        Assert.False(row.Accepted);
        Assert.True(row.NeedsAction);
        Assert.Equal("أُرسل — لم يُشاهَد", row.StatusLabel);
    }

    [Fact]
    public void ViewedWithoutAccepting_IsDistinctFromNeverOpened()
    {
        var row = Row(viewed: Sent.AddHours(2));

        Assert.True(row.Viewed);
        Assert.True(row.NeedsAction);
        Assert.Equal("شوهد — بلا موافقة", row.StatusLabel);
    }

    [Fact]
    public void Accepted_ClosesTheAction()
    {
        var row = Row(viewed: Sent.AddHours(2), accepted: Sent.AddHours(3));

        Assert.True(row.Accepted);
        Assert.False(row.NeedsAction);
        Assert.Equal("موافق", row.StatusLabel);
    }

    [Fact]
    public void Declined_IsFinalAndOutranksAcceptedInDisplay()
    {
        // حالةٌ لا تنشأ عادةً (الكتابة تمنعها)، لكن العرض يجب أن يُظهر الرفض لا أن
        // يبتلعه — صفٌّ يحمل الختمين يعني تلفاً يستحق أن يُرى.
        var row = Row(viewed: Sent, accepted: Sent.AddHours(1), declined: Sent.AddHours(2));

        Assert.Equal("رفض", row.StatusLabel);
        Assert.False(row.NeedsAction);
    }

    [Fact]
    public void DeclinedWithoutAccepting_IsFinal()
    {
        var row = Row(viewed: Sent, declined: Sent.AddHours(1));

        Assert.True(row.Declined);
        Assert.False(row.NeedsAction);
        Assert.Equal("رفض", row.StatusLabel);
    }

    [Fact]
    public void AcceptingWithoutViewing_StillCounts()
    {
        // الموافقة من الإشعار مباشرةً واقعةٌ ممكنة؛ المهم ألا تُعدّ «معلّقة».
        var row = Row(accepted: Sent.AddMinutes(30));

        Assert.False(row.Viewed);
        Assert.True(row.Accepted);
        Assert.False(row.NeedsAction);
        Assert.Equal("موافق", row.StatusLabel);
    }
}
