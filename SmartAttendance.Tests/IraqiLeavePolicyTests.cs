using SmartAttendance.Domain.Enums;
using SmartAttendance.Domain.Leave;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// عقد الأثر المالي للإجازة <see cref="IraqiLeavePolicy.IsPaid"/> — مصدر الحقيقة
/// الوحيد لربط الإجازات بالمسير: غير المدفوعة وحدها تُخصم. دالة نقية فالتغطية رخيصة.
/// </summary>
public class IraqiLeavePolicyTests
{
    [Theory]
    [InlineData(LeaveType.Annual)]
    [InlineData(LeaveType.Sick)]
    [InlineData(LeaveType.Emergency)]
    [InlineData(LeaveType.Official)]
    public void IsPaid_TrueForAllButUnpaid(LeaveType type) =>
        Assert.True(IraqiLeavePolicy.IsPaid(type));

    [Fact]
    public void IsPaid_FalseForUnpaid() =>
        Assert.False(IraqiLeavePolicy.IsPaid(LeaveType.Unpaid));
}
