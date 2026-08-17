using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>مدقق الحركات — كل قاعدة تُثبَت بحالة إيجابية وأخرى سلبية على صفوف بالذاكرة.</summary>
public class PayrollTransactionsAuditorTests
{
    private static (int, int, string, string, decimal, string, string, decimal, decimal?, decimal?, int, int, string, string, bool) Row(
        int id, int emp, string type, string item, decimal amount, decimal? hours = null, decimal? days = null,
        int year = 2026, int month = 8, string status = "Approved", bool periodLocked = false, decimal basic = 3000m)
        => (id, emp, $"E{emp}", $"موظف {emp}", basic, type, item, amount, hours, days, year, month, status, $"R{id}", periodLocked);

    [Fact]
    public void CleanRows_NoFindings()
    {
        var rows = new[]
        {
            Row(1, 1, "Income", "بدل", 100),
            Row(2, 1, "Overtime", "إضافي", 200, hours: 10),
            Row(3, 2, "Deduction", "جزاء", 50)
        };
        Assert.Empty(PayrollTransactionsAuditor.Analyze(rows));
    }

    [Fact]
    public void Duplicate_Detected_OncePerGroup()
    {
        var rows = new[] { Row(1, 1, "Income", "بدل", 100), Row(2, 1, "Income", "بدل", 100), Row(3, 1, "Income", "بدل", 100) };
        var f = PayrollTransactionsAuditor.Analyze(rows);
        Assert.Single(f, x => x.Rule == "مكرر");
        Assert.Contains("3 حركات", f[0].Detail);
    }

    [Fact]
    public void ZeroAmount_WithoutHoursOrDays_Flagged_ButWithHoursNot()
    {
        var flagged = PayrollTransactionsAuditor.Analyze(new[] { Row(1, 1, "Income", "بدل", 0) });
        Assert.Contains(flagged, x => x.Rule == "مبلغ صفري");

        var ok = PayrollTransactionsAuditor.Analyze(new[] { Row(2, 1, "Overtime", "إضافي", 0, hours: 5) });
        Assert.DoesNotContain(ok, x => x.Rule == "مبلغ صفري");
    }

    [Fact]
    public void NegativeIncomeOrDeduction_Flagged()
    {
        var f = PayrollTransactionsAuditor.Analyze(new[] { Row(1, 1, "Income", "بدل", -50), Row(2, 1, "Deduction", "جزاء", -20) });
        Assert.Equal(2, f.Count(x => x.Rule == "سالب"));
    }

    [Fact]
    public void ExcessiveOvertimeHours_Flagged()
    {
        var f = PayrollTransactionsAuditor.Analyze(new[] { Row(1, 1, "Overtime", "إضافي", 900, hours: 150) });
        Assert.Contains(f, x => x.Rule == "ساعات إضافي مفرطة");
        var ok = PayrollTransactionsAuditor.Analyze(new[] { Row(2, 1, "Overtime", "إضافي", 900, hours: 100) });
        Assert.DoesNotContain(ok, x => x.Rule == "ساعات إضافي مفرطة");
    }

    [Fact]
    public void DeductionAboveBasic_Flagged()
    {
        var f = PayrollTransactionsAuditor.Analyze(new[] { Row(1, 1, "Deduction", "جزاء", 3500, basic: 3000) });
        Assert.Contains(f, x => x.Rule == "اقتطاع يفوق الأساسي");
    }

    [Fact]
    public void DraftInLockedPeriod_Flagged_ApprovedNot()
    {
        var f = PayrollTransactionsAuditor.Analyze(new[] { Row(1, 1, "Income", "بدل", 100, status: "Draft", periodLocked: true) });
        Assert.Contains(f, x => x.Rule == "مسودّة بفترة مقفلة");
        var ok = PayrollTransactionsAuditor.Analyze(new[] { Row(2, 1, "Income", "بدل", 100, status: "Approved", periodLocked: true) });
        Assert.DoesNotContain(ok, x => x.Rule == "مسودّة بفترة مقفلة");
    }

    [Fact]
    public void Summarize_CountsPerEmployee_Descending()
    {
        var rows = new[]
        {
            Row(1, 1, "Income", "بدل", 0), Row(2, 1, "Income", "x", -1),
            Row(3, 2, "Income", "بدل", 0)
        };
        var s = PayrollTransactionsAuditor.Summarize(PayrollTransactionsAuditor.Analyze(rows));
        Assert.Equal(2, s.Count);
        Assert.Equal(1, s[0].EmployeeId);
        Assert.Equal(2, s[0].Cases);
    }
}
