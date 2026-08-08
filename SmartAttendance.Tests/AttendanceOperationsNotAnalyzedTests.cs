using SmartAttendance.Application.AttendanceProcessing.ViewModels;
using SmartAttendance.Web.Pages.AttendanceOperations;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حارس الموجة W3 — توحيد مصدر حقيقة الحضور: شاشة «مراقبة الحضور»
/// (<c>AttendanceOperations</c>) لم تعُد تعرض حكم المحرّك القديم للصفوف بلا يومية
/// محلَّلة، بل حالة «غير محلَّل» بلا احتساب. الأيام المحلَّلة تبقى بلا تغيّر لأنها
/// تأتي من <c>DayAttendanceStore</c> (مصدر ما يقرؤه المسير) — فلا رقم رواتب يتأثّر.
/// </summary>
public class AttendanceOperationsNotAnalyzedTests
{
    private static AttendanceProcessingResultViewModel LegacyRow() => new()
    {
        AttendanceRecordId = 7,
        EmployeeId = 42,
        EmployeeNo = "E-042",
        EmployeeName = "موظف تجريبي",
        AttendanceDate = new System.DateOnly(2026, 8, 3),
        ShiftCode = "M",
        ShiftName = "صباحي",
        CheckIn = new System.DateTime(2026, 8, 3, 8, 40, 0),
        CheckOut = new System.DateTime(2026, 8, 3, 16, 10, 0),
        WorkingHours = 7.5m,
        LateMinutes = 40,
        EarlyLeaveMinutes = 20,
        CalculatedStatus = "Late",
    };

    [Fact]
    public void BuildNotAnalyzedRow_BlanksTheLegacyVerdict_ButKeepsIdentityAndRawTimes()
    {
        var stripped = IndexModel.BuildNotAnalyzedRow(LegacyRow());

        // الهُويّة والأوقات الخام محفوظة — الموظف لا يختفي من الكشف.
        Assert.Equal("E-042", stripped.EmployeeNo);
        Assert.Equal(42, stripped.EmployeeId);
        Assert.Equal(new System.DateOnly(2026, 8, 3), stripped.AttendanceDate);
        Assert.Equal("صباحي", stripped.ShiftName);
        Assert.Equal(new System.DateTime(2026, 8, 3, 8, 40, 0), stripped.CheckIn);
        Assert.Equal(new System.DateTime(2026, 8, 3, 16, 10, 0), stripped.CheckOut);

        // حقول الحكم مُفرَّغة — لا محرّك قديم.
        Assert.Null(stripped.WorkingHours);
        Assert.Equal(0, stripped.LateMinutes);
        Assert.Null(stripped.EarlyLeaveMinutes);
        Assert.Equal(IndexModel.NotAnalyzedStatus, stripped.CalculatedStatus);
    }

    [Fact]
    public void AutoStatusText_NotAnalyzedSentinel_TakesPrecedenceOverPunches()
    {
        // حتى مع بصمتَي دخول وخروج، الحالة تبقى «غير محلَّل» — لا اشتقاق حكم.
        var text = IndexModel.ProcessingAutoStatusText(
            new System.DateTime(2026, 8, 3, 8, 0, 0),
            new System.DateTime(2026, 8, 3, 16, 0, 0),
            IndexModel.NotAnalyzedStatus);

        Assert.Equal("غير محلَّل", text);
    }

    [Theory]
    [InlineData("Late")]
    [InlineData("Weekly Off")]
    [InlineData("Holiday")]
    [InlineData("Leave")]
    public void AutoStatusText_AnalysedStatuses_AreUnchanged(string calculated)
    {
        // الأيام المحلَّلة تسلك كما كانت — الموجة لم تمسّ مسار official.
        var text = IndexModel.ProcessingAutoStatusText(
            new System.DateTime(2026, 8, 3, 9, 0, 0),
            new System.DateTime(2026, 8, 3, 17, 0, 0),
            calculated);

        Assert.NotEqual("غير محلَّل", text);
    }
}
