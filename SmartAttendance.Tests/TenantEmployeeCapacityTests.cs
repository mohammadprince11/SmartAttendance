using SmartAttendance.Web.Infrastructure.Platform;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class TenantEmployeeCapacityTests
{
    [Theory]
    [InlineData(9, 10, true)]
    [InlineData(10, 10, false)]
    [InlineData(11, 10, false)]
    [InlineData(0, 0, false)]
    [InlineData(10, 100, true)]
    public void Capacity_UsesConfiguredLimit(long used, int max, bool allowed) =>
        Assert.Equal(allowed, new TenantEmployeeCapacity(used, max).CanAdd);

    [Fact]
    public void Message_ExplainsActualUsage() =>
        Assert.Contains("10 من 10", TenantEmployeeCapacity.LimitMessage(new(10, 10)));

    [Fact]
    public void MissingLicense_FailsClosed() =>
        Assert.Contains("غير مكتمل", TenantEmployeeCapacity.LimitMessage(null));

    [Fact]
    public async Task MissingTenant_DoesNotQueryDatabase() =>
        Assert.Null(await TenantEmployeeCapacity.LoadAsync(null!, 0));

    [Fact]
    public void UnrelatedFailure_IsNotReportedAsCapacityLimit() =>
        Assert.False(TenantEmployeeCapacity.IsLimitException(new InvalidOperationException("Unrelated")));
}
