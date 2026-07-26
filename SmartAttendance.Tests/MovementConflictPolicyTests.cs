using SmartAttendance.Domain.Attendance;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// عقد رصد «التعارض مع الحركة» <see cref="MovementConflictPolicy"/> — الشريحة الأولى
/// النقية من توصيل تبويب «التعارض». دالة نقية بلا قاعدة بيانات فالتغطية رخيصة وكاملة.
/// الحارس الأهم: القاعدة المعطّلة (الافتراض) لا تُطلق شيئاً مهما كان التجاوز.
/// </summary>
public class MovementConflictPolicyTests
{
    private static readonly TimeOnly Start = new(14, 0); // بداية نافذة المغادرة 2:00 م
    private static readonly TimeOnly End = new(15, 0);   // نهاية نافذة المغادرة 3:00 م

    // ── الحارس: قاعدة معطّلة لا أثر لها إطلاقاً ──────────────────────────────

    [Fact]
    public void EarlyLeave_DisabledRule_NeverTriggers()
    {
        var rule = new MovementConflictPolicy.Rule(Enabled: false, MovementConflictPolicy.ActionDeduction, 50m);
        var result = MovementConflictPolicy.EvaluateEarlyLeave(rule, Start, new TimeOnly(13, 0));
        Assert.False(result.Triggered);
    }

    [Fact]
    public void LateReturn_DisabledRule_NeverTriggers()
    {
        var rule = new MovementConflictPolicy.Rule(Enabled: false, MovementConflictPolicy.ActionPermission, 1m);
        var result = MovementConflictPolicy.EvaluateLateReturn(rule, End, new TimeOnly(16, 0));
        Assert.False(result.Triggered);
    }

    // ── الخروج المبكّر ──────────────────────────────────────────────────────

    [Fact]
    public void EarlyLeave_LeftBeforeWindow_Triggers_WithConfiguredPenalty()
    {
        var rule = new MovementConflictPolicy.Rule(Enabled: true, MovementConflictPolicy.ActionDeduction, 25m);
        var result = MovementConflictPolicy.EvaluateEarlyLeave(rule, Start, new TimeOnly(13, 30));

        Assert.True(result.Triggered);
        Assert.Equal(MovementConflictPolicy.ActionDeduction, result.Action);
        Assert.Equal(25m, result.Value);        // القيمة ثابتة من الإعداد لا من التجاوز
        Assert.Equal(30, result.MinutesOff);    // غادر 30 دقيقة مبكّراً
    }

    [Fact]
    public void EarlyLeave_LeftExactlyAtStart_NoConflict()
    {
        var rule = new MovementConflictPolicy.Rule(Enabled: true, MovementConflictPolicy.ActionDeduction, 25m);
        var result = MovementConflictPolicy.EvaluateEarlyLeave(rule, Start, Start);
        Assert.False(result.Triggered);
    }

    [Fact]
    public void EarlyLeave_LeftAfterStart_NoConflict()
    {
        var rule = new MovementConflictPolicy.Rule(Enabled: true, MovementConflictPolicy.ActionDeduction, 25m);
        var result = MovementConflictPolicy.EvaluateEarlyLeave(rule, Start, new TimeOnly(14, 20));
        Assert.False(result.Triggered);
    }

    // ── العودة المتأخّرة ────────────────────────────────────────────────────

    [Fact]
    public void LateReturn_ReturnedAfterWindow_Triggers_AsPermissionHours()
    {
        var rule = new MovementConflictPolicy.Rule(Enabled: true, MovementConflictPolicy.ActionPermission, 2m);
        var result = MovementConflictPolicy.EvaluateLateReturn(rule, End, new TimeOnly(15, 45));

        Assert.True(result.Triggered);
        Assert.Equal(MovementConflictPolicy.ActionPermission, result.Action);
        Assert.Equal(2m, result.Value);
        Assert.Equal(45, result.MinutesOff);    // عاد متأخّراً 45 دقيقة
    }

    [Fact]
    public void LateReturn_ReturnedExactlyAtEnd_NoConflict()
    {
        var rule = new MovementConflictPolicy.Rule(Enabled: true, MovementConflictPolicy.ActionPermission, 2m);
        var result = MovementConflictPolicy.EvaluateLateReturn(rule, End, End);
        Assert.False(result.Triggered);
    }

    [Fact]
    public void LateReturn_ReturnedBeforeEnd_NoConflict()
    {
        var rule = new MovementConflictPolicy.Rule(Enabled: true, MovementConflictPolicy.ActionPermission, 2m);
        var result = MovementConflictPolicy.EvaluateLateReturn(rule, End, new TimeOnly(14, 50));
        Assert.False(result.Triggered);
    }
}
