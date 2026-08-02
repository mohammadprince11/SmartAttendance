using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// الجزاء التأديبي صار كائناً يقود حساباً مالياً — فكل قرارٍ فيه يُختبر، لا
/// يُجرَّب على قسيمة موظف.
/// </summary>
public class PenaltyRuleModelTests
{
    // ── نوع الإجراء ──────────────────────────────────────────────────────────

    [Fact]
    public void Only_salary_deduction_is_financial()
    {
        Assert.True(PenaltyAction.IsFinancial(PenaltyAction.SalaryDeduction));

        foreach (var other in new[]
        {
            PenaltyAction.VerbalWarning, PenaltyAction.WrittenWarning, PenaltyAction.Warning,
            PenaltyAction.FinalWarning, PenaltyAction.RaiseDenial, PenaltyAction.Termination
        })
        {
            Assert.False(PenaltyAction.IsFinancial(other));
        }
    }

    [Fact]
    public void Unknown_action_falls_back_to_the_lightest_not_to_a_deduction()
    {
        // نوعٌ مجهول يجب ألا يخصم من راتب أحد.
        Assert.Equal(PenaltyAction.VerbalWarning, PenaltyAction.Normalize("SomethingElse"));
        Assert.False(PenaltyAction.IsFinancial("SomethingElse"));
    }

    [Fact]
    public void Non_financial_action_forces_impact_to_none_even_if_a_value_was_declared()
    {
        // إنذارٌ مصحوب بقيمة مالية كان يخصم بلا إجراءٍ يبرّره.
        Assert.Equal("None", PenaltyAction.FinancialImpact(PenaltyAction.Warning, "Days"));
        Assert.Equal("Days", PenaltyAction.FinancialImpact(PenaltyAction.SalaryDeduction, "Days"));
        Assert.Equal("Days", PenaltyAction.FinancialImpact(PenaltyAction.SalaryDeduction, null));
    }

    [Fact]
    public void Termination_is_the_only_type_that_asks_for_a_stop_reason() =>
        Assert.True(PenaltyAction.IsTermination(PenaltyAction.Termination)
            && !PenaltyAction.IsTermination(PenaltyAction.FinalWarning));

    // ── وعاء الخصم ───────────────────────────────────────────────────────────

    private static SalaryBaseComposer.Amounts Salary() => new()
    {
        Basic = 900_000m,
        Allowances = 300_000m,
        Gross = 1_200_000m
    };

    [Fact]
    public void Empty_pool_keeps_todays_behaviour_basic_at_full()
    {
        // انحرافٌ هنا يظهر بكل قسيمة — الفراغ يجب أن يعني الأساسي كاملاً.
        Assert.Equal(900_000m, PenaltyBasePool.Amount(Salary(), PenaltyBasePool.Parse(null)));
        Assert.Equal(900_000m, PenaltyBasePool.Amount(Salary(), PenaltyBasePool.Parse("")));
        Assert.Equal(900_000m, PenaltyBasePool.Amount(Salary(), PenaltyBasePool.Parse("[]")));
    }

    [Fact]
    public void Corrupt_pool_json_falls_back_instead_of_throwing() =>
        Assert.Equal(900_000m, PenaltyBasePool.Amount(Salary(), PenaltyBasePool.Parse("{not json")));

    [Fact]
    public void Pool_sums_each_component_at_its_own_percentage()
    {
        var json = PenaltyBasePool.Serialize(new[]
        {
            new PenaltyBasePool.Member { Key = "Basic", Percent = 50m },
            new PenaltyBasePool.Member { Key = "Allowances", Percent = 100m }
        });

        // 450,000 + 300,000
        Assert.Equal(750_000m, PenaltyBasePool.Amount(Salary(), PenaltyBasePool.Parse(json)));
    }

    [Fact]
    public void Pool_percentages_may_exceed_one_hundred_in_total()
    {
        var members = new[]
        {
            new PenaltyBasePool.Member { Key = "Basic", Percent = 100m },
            new PenaltyBasePool.Member { Key = "Allowances", Percent = 100m }
        };

        Assert.Equal(1_200_000m, PenaltyBasePool.Amount(Salary(), members));
    }

    [Fact]
    public void Unknown_component_in_a_saved_pool_is_ignored_not_fatal()
    {
        var members = new[]
        {
            new PenaltyBasePool.Member { Key = "Basic", Percent = 100m },
            new PenaltyBasePool.Member { Key = "ComponentThatWasRemoved", Percent = 100m }
        };

        Assert.Equal(900_000m, PenaltyBasePool.Amount(Salary(), members));
    }

    // ── مقام أيام العمل ──────────────────────────────────────────────────────

    [Fact]
    public void Fixed_thirty_is_the_default_and_reproduces_the_old_divisor()
    {
        Assert.Equal(30m, WorkDaysBasis.Divisor(null, 0, 31));
        Assert.Equal(30m, WorkDaysBasis.Divisor(WorkDaysBasis.Fixed, 30, 31));
        Assert.Equal(30_000m, WorkDaysBasis.DailyRate(900_000m, WorkDaysBasis.Divisor(null, 0, 31)));
    }

    [Fact]
    public void Period_basis_follows_the_real_month_length() =>
        Assert.Equal(31m, WorkDaysBasis.Divisor(WorkDaysBasis.Period, 30, 31));

    [Fact]
    public void Fiscal_year_basis_is_the_365_over_12_average() =>
        Assert.Equal(30.4167m, WorkDaysBasis.Divisor(WorkDaysBasis.FiscalYear, 30, 31));

    [Fact]
    public void Excluding_holidays_shrinks_the_divisor_and_raises_the_daily_rate()
    {
        var divisor = WorkDaysBasis.Divisor(
            WorkDaysBasis.Period, 30, daysInPeriod: 30,
            companyHolidays: 2, restDays: 4, excludeMode: WorkDaysBasis.ExcludeBoth);

        Assert.Equal(24m, divisor);
    }

    [Fact]
    public void Fiscal_year_average_ignores_holiday_exclusion() =>
        // معدّل نظاميّ لا فترة تقويمية — لا يُطرح منه عطل.
        Assert.Equal(30.4167m, WorkDaysBasis.Divisor(
            WorkDaysBasis.FiscalYear, 30, 30, 5, 4, WorkDaysBasis.ExcludeBoth));

    [Fact]
    public void Divisor_never_drops_below_one_day()
    {
        // شركة تستثني كل أيام الشهر: القسمة على صفر تبتلع القسيمة كلّها.
        var divisor = WorkDaysBasis.Divisor(
            WorkDaysBasis.Period, 30, 30, companyHolidays: 30, restDays: 10,
            excludeMode: WorkDaysBasis.ExcludeBoth);

        Assert.Equal(1m, divisor);
    }

    [Fact]
    public void Daily_rate_of_a_zero_divisor_is_zero_not_infinity() =>
        Assert.Equal(0m, WorkDaysBasis.DailyRate(900_000m, 0m));

    // ── سياسة الإسقاط ────────────────────────────────────────────────────────

    private static readonly DateOnly Action = new(2026, 3, 10);

    [Fact]
    public void Once_from_the_action_date_adds_the_period_to_it() =>
        Assert.Equal(new DateOnly(2026, 9, 10), PenaltyDropPolicy.DropOn(
            PenaltyDropPolicy.ModeOnce, 6, PenaltyDropPolicy.UnitMonths,
            PenaltyDropPolicy.AnchorActionDate, Action));

    [Fact]
    public void Days_and_years_units_are_honoured()
    {
        Assert.Equal(new DateOnly(2026, 9, 6), PenaltyDropPolicy.DropOn(
            PenaltyDropPolicy.ModeOnce, 180, PenaltyDropPolicy.UnitDays,
            PenaltyDropPolicy.AnchorActionDate, Action));

        Assert.Equal(new DateOnly(2027, 3, 10), PenaltyDropPolicy.DropOn(
            PenaltyDropPolicy.ModeOnce, 1, PenaltyDropPolicy.UnitYears,
            PenaltyDropPolicy.AnchorActionDate, Action));
    }

    [Fact]
    public void Fiscal_year_anchor_drops_everyone_together_not_per_violation()
    {
        // الفارق العملي: شركة تُسقط بنهاية السنة لا بعد سنةٍ من كل مخالفة.
        var dropOn = PenaltyDropPolicy.DropOn(
            PenaltyDropPolicy.ModeOnce, 1, PenaltyDropPolicy.UnitDays,
            PenaltyDropPolicy.AnchorFiscalYearEnd, Action,
            fiscalYearEnd: new DateOnly(2026, 12, 31));

        Assert.Equal(new DateOnly(2027, 1, 1), dropOn);
    }

    [Fact]
    public void A_past_anchor_never_yields_a_drop_date_before_the_violation_itself()
    {
        // مرساةٌ مضت ومدّةٌ قصيرة كانتا ستُسقطان المخالفة يوم تسجيلها: 2024-12-31
        // زائد شهر = 2025-01-31، أي بالماضي. تُعاد المرساة لتاريخ الإجراء.
        var dropOn = PenaltyDropPolicy.DropOn(
            PenaltyDropPolicy.ModeOnce, 1, PenaltyDropPolicy.UnitMonths,
            PenaltyDropPolicy.AnchorFiscalYearEnd, Action,
            fiscalYearEnd: new DateOnly(2024, 12, 31));

        Assert.True(dropOn > Action);
        Assert.Equal(new DateOnly(2026, 4, 10), dropOn);
    }

    [Fact]
    public void Periodic_advances_past_the_action_date_in_whole_steps()
    {
        var dropOn = PenaltyDropPolicy.DropOn(
            PenaltyDropPolicy.ModePeriodic, 3, PenaltyDropPolicy.UnitMonths,
            PenaltyDropPolicy.AnchorFiscalYearEnd, Action,
            fiscalYearEnd: new DateOnly(2025, 12, 31));

        // أوّل حدٍّ ربعيّ بعد تاريخ الإجراء: 2025-12-31 ⟵ 2026-03-31.
        Assert.Equal(new DateOnly(2026, 3, 31), dropOn);

        // وخطوةٌ أخرى حين يقع الإجراء بعد الحدّ الأول.
        Assert.Equal(new DateOnly(2026, 6, 30), PenaltyDropPolicy.DropOn(
            PenaltyDropPolicy.ModePeriodic, 3, PenaltyDropPolicy.UnitMonths,
            PenaltyDropPolicy.AnchorFiscalYearEnd, new DateOnly(2026, 4, 15),
            fiscalYearEnd: new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void A_zero_period_means_it_never_drops() =>
        Assert.Null(PenaltyDropPolicy.DropOn(
            PenaltyDropPolicy.ModeOnce, 0, PenaltyDropPolicy.UnitMonths,
            PenaltyDropPolicy.AnchorActionDate, Action));

    [Fact]
    public void IsDropped_compares_against_the_reference_date()
    {
        var dropOn = new DateOnly(2026, 9, 10);

        Assert.False(PenaltyDropPolicy.IsDropped(dropOn, new DateOnly(2026, 9, 9)));
        Assert.True(PenaltyDropPolicy.IsDropped(dropOn, new DateOnly(2026, 9, 10)));
        Assert.False(PenaltyDropPolicy.IsDropped(null, new DateOnly(2099, 1, 1)));
    }
}
