using SmartAttendance.Application.AttendanceProcessing.ViewModels;
using SmartAttendance.Web.Infrastructure.Hrms;
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

    [Fact]
    public void BuildNotAnalyzedRow_FromRawPunch_BlanksVerdict_ButKeepsIdentityAndRawTimes()
    {
        // W9 — التعداد من البصمات الخام مباشرةً (لا محرّك قديم): الصفّ الخام يُترجَم
        // «غير محلَّل» بنفس منطق التفريغ المُختبَر، مع حفظ الهُويّة والأوقات.
        var raw = new DayAttendanceStore.RawPunchRow
        {
            AttendanceRecordId = 11,
            EmployeeId = 42,
            EmployeeNo = "E-042",
            EmployeeName = "موظف تجريبي",
            AttendanceDate = new System.DateOnly(2026, 8, 3),
            CheckIn = new System.DateTime(2026, 8, 3, 8, 40, 0),
            CheckOut = new System.DateTime(2026, 8, 3, 16, 10, 0),
            Source = "Device",
        };

        var row = IndexModel.BuildNotAnalyzedRow(raw);

        Assert.Equal("E-042", row.EmployeeNo);
        Assert.Equal(42, row.EmployeeId);
        Assert.Equal(new System.DateOnly(2026, 8, 3), row.AttendanceDate);
        Assert.Equal(new System.DateTime(2026, 8, 3, 8, 40, 0), row.CheckIn);
        Assert.Equal(new System.DateTime(2026, 8, 3, 16, 10, 0), row.CheckOut);
        Assert.Equal("Device", row.Source);
        Assert.False(row.MissingCheckOut);

        // حقول الحكم مُفرَّغة — لا محرّك قديم.
        Assert.Null(row.WorkingHours);
        Assert.Equal(0, row.LateMinutes);
        Assert.Null(row.EarlyLeaveMinutes);
        Assert.Equal(IndexModel.NotAnalyzedStatus, row.CalculatedStatus);
    }

    [Fact]
    public void BuildNotAnalyzedRow_FromRawPunch_NoCheckout_FlagsMissingCheckOut()
    {
        // بصمة دخول بلا خروج: يُعلَّم «بصمة خروج ناقصة» وتبقى الحالة «غير محلَّل».
        var raw = new DayAttendanceStore.RawPunchRow
        {
            EmployeeId = 7,
            EmployeeNo = "E-007",
            EmployeeName = "موظف",
            AttendanceDate = new System.DateOnly(2026, 8, 4),
            CheckIn = new System.DateTime(2026, 8, 4, 9, 0, 0),
            CheckOut = null,
            Source = "Manual",
        };

        var row = IndexModel.BuildNotAnalyzedRow(raw);

        Assert.True(row.MissingCheckOut);
        Assert.Equal(IndexModel.NotAnalyzedStatus, row.CalculatedStatus);
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
