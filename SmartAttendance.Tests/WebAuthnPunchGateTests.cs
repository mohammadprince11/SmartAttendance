using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// اختبارات بوابة التأكيد البيولوجي للبصم الأونلاين: تُحجب البصمة فقط حين تكون
/// الراية مفعّلة وللموظف مفتاح نشط وبلا إثبات لحظي — موظف بلا مفتاح معتمد معفى
/// (نفس نمط إنفاذ النطاق الجغرافي)، والراية معطّلة افتراضياً.
/// </summary>
public class WebAuthnPunchGateTests
{
    [Theory]
    // الراية معطّلة ⟹ لا حجب أياً كانت بقية الحالة
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    // الراية مفعّلة + بلا مفتاح نشط ⟹ معفى (لا حجب)
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, false)]
    // الراية مفعّلة + مفتاح نشط ⟹ يُحجب بلا إثبات ويمرّ به
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void BiometricGate_TruthTable(bool flag, bool hasKey, bool verified, bool blocked) =>
        Assert.Equal(blocked, OnlinePunchStore.BiometricGateBlocks(flag, hasKey, verified));

    [Fact]
    public void PunchStatus_HasBiometricRequired() =>
        Assert.True(Enum.IsDefined(OnlinePunchStore.PunchStatus.BiometricRequired));

    [Fact]
    public void RequireBiometricKey_MatchesSettingsNamingPattern() =>
        Assert.Equal("Attendance.OnlinePunch.RequireBiometric", OnlinePunchStore.RequireBiometricKey);
}
