using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات منطق العقود.
///
/// أهمّها <see cref="OpenEndedContract_HasNoRemainingDays_NotZero"/>: صفرٌ يعني
/// «منتهٍ» وهو **عكس** معنى العقد غير المحدود تماماً — والخلط بينهما يجعل عقوداً
/// مفتوحة تظهر منتهيةً بشاشة التنبيهات فيُجدَّد ما لا يحتاج تجديداً.
///
/// و<see cref="RenewStartsAfterPreviousEnd_ExtendKeepsOriginalStart"/> يحرس الفرق
/// الذي يقرّر المال: التمديد خدمة متصلة، والتجديد عقد جديد.
/// </summary>
public class ContractPolicyTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 1);

    // ── الأيام المتبقية ────────────────────────────────────────────────────────

    [Fact]
    public void OpenEndedContract_HasNoRemainingDays_NotZero()
    {
        Assert.Null(ContractPolicy.RemainingDays(null, AsOf));
        Assert.Equal("غير محدود", ContractPolicy.RemainingLabel(null, AsOf));
    }

    [Fact]
    public void ExpiredContract_ClampsToZero_NeverNegative()
    {
        Assert.Equal(0, ContractPolicy.RemainingDays(new DateOnly(2014, 8, 1), AsOf));
    }

    [Fact]
    public void RemainingDays_CountsCalendarDaysToEnd()
    {
        Assert.Equal(30, ContractPolicy.RemainingDays(new DateOnly(2026, 8, 31), AsOf));
        Assert.Equal(0, ContractPolicy.RemainingDays(AsOf, AsOf));
    }

    // ── سريان العقد ────────────────────────────────────────────────────────────

    [Fact]
    public void IsCurrent_TrueOnlyBetweenBoundsInclusive()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 12, 31);

        Assert.True(ContractPolicy.IsCurrent(from, to, AsOf));
        Assert.True(ContractPolicy.IsCurrent(from, to, from));
        Assert.True(ContractPolicy.IsCurrent(from, to, to));
        Assert.False(ContractPolicy.IsCurrent(from, to, new DateOnly(2025, 12, 31)));
        Assert.False(ContractPolicy.IsCurrent(from, to, new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void OpenEndedContract_IsCurrentOnceStarted()
    {
        Assert.True(ContractPolicy.IsCurrent(new DateOnly(2021, 1, 2), null, AsOf));
        Assert.False(ContractPolicy.IsCurrent(new DateOnly(2027, 1, 2), null, AsOf));
    }

    // ── اشتقاق تاريخ الانتهاء ──────────────────────────────────────────────────

    [Fact]
    public void DeriveEndDate_SubtractsOneDay_SoOneYearEndsOnDecember31()
    {
        // خطأ اليوم الواحد هنا يزحزح كل تنبيهات الانتهاء ومدة الخدمة.
        var end = ContractPolicy.DeriveEndDate(new DateOnly(2022, 1, 1), 12);

        Assert.Equal(new DateOnly(2022, 12, 31), end);
    }

    [Fact]
    public void DeriveEndDate_WithoutDuration_MeansOpenEnded_NotInventedDate()
    {
        Assert.Null(ContractPolicy.DeriveEndDate(new DateOnly(2022, 1, 1), null));
        Assert.Null(ContractPolicy.DeriveEndDate(new DateOnly(2022, 1, 1), 0));
        Assert.Null(ContractPolicy.DeriveEndDate(new DateOnly(2022, 1, 1), -3));
    }

    // ── تمديد مقابل تجديد ──────────────────────────────────────────────────────

    [Fact]
    public void RenewStartsAfterPreviousEnd_ExtendKeepsOriginalStart()
    {
        var start = new DateOnly(2022, 1, 1);
        var end = new DateOnly(2026, 12, 31);

        Assert.Equal(start, ContractPolicy.NextStartDate(ContractPolicy.MovementExtend, start, end));
        Assert.Equal(new DateOnly(2027, 1, 1), ContractPolicy.NextStartDate(ContractPolicy.MovementRenew, start, end));
    }

    [Fact]
    public void RenewWithoutPreviousEnd_KeepsGivenStart()
    {
        var start = new DateOnly(2026, 3, 1);

        Assert.Equal(start, ContractPolicy.NextStartDate(ContractPolicy.MovementRenew, start, null));
    }

    // ── الخدمة المتصلة ─────────────────────────────────────────────────────────

    [Fact]
    public void ContinuousService_SpansBackToBackContracts()
    {
        var contracts = new List<(DateOnly, DateOnly?)>
        {
            (new DateOnly(2024, 8, 1), new DateOnly(2025, 7, 31)),
            (new DateOnly(2025, 8, 1), new DateOnly(2026, 7, 31)),
            (new DateOnly(2026, 8, 1), new DateOnly(2027, 7, 31))
        };

        Assert.Equal(24, ContractPolicy.ContinuousServiceMonths(contracts, AsOf));
    }

    [Fact]
    public void ContinuousService_BreaksOnGap_SoSeveredServiceIsNotCounted()
    {
        // هذه الحالة بالضبط ما كان يخطئ فيه حساب نهاية الخدمة قبل وجود سجلّ عقود.
        var contracts = new List<(DateOnly, DateOnly?)>
        {
            (new DateOnly(2013, 8, 2), new DateOnly(2014, 8, 1)),
            (new DateOnly(2025, 8, 1), new DateOnly(2026, 7, 31)),
            (new DateOnly(2026, 8, 1), new DateOnly(2027, 7, 31))
        };

        Assert.Equal(12, ContractPolicy.ContinuousServiceMonths(contracts, AsOf));
    }

    [Fact]
    public void ContinuousService_EmptyRegister_IsZero()
    {
        Assert.Equal(0, ContractPolicy.ContinuousServiceMonths(Array.Empty<(DateOnly, DateOnly?)>(), AsOf));
    }

    [Fact]
    public void ContinuousService_FutureOnlyContract_IsZeroNotNegative()
    {
        var contracts = new List<(DateOnly, DateOnly?)> { (new DateOnly(2027, 1, 1), null) };

        Assert.Equal(0, ContractPolicy.ContinuousServiceMonths(contracts, AsOf));
    }

    [Fact]
    public void MovementLabels_AreDistinct_SoTheUiCannotConflateThem()
    {
        Assert.Equal("تمديد العقد الحالي", ContractPolicy.MovementLabel(ContractPolicy.MovementExtend));
        Assert.Equal("تجديد عقد", ContractPolicy.MovementLabel(ContractPolicy.MovementRenew));
        Assert.Equal("تعديل عقد", ContractPolicy.MovementLabel(ContractPolicy.MovementAmend));
    }
}
