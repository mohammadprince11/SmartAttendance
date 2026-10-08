using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public sealed class ApprovalTemplateNavigationTests
{
    [Theory]
    [InlineData(null, "SelfService")]
    [InlineData("invalid", "SelfService")]
    [InlineData("supervisor", "Supervisor")]
    public void Audience_IsNormalized(string? value, string expected)
        => Assert.Equal(expected, ApprovalTemplateNavigation.NormalizeAudience(value));

    [Fact]
    public void Catalog_PartitionsSupportedTypesWithoutDuplicatesOrDummyTypes()
    {
        var self = ApprovalTemplateStore.RequestTypes.Where(t => ApprovalTemplateNavigation.VisibleIn(t.Key, "SelfService")).ToArray();
        var supervisor = ApprovalTemplateStore.RequestTypes.Where(t => ApprovalTemplateNavigation.VisibleIn(t.Key, "Supervisor")).ToArray();
        Assert.NotEmpty(self);
        Assert.NotEmpty(supervisor);
        Assert.Empty(self.Select(t => t.Key).Intersect(supervisor.Select(t => t.Key)));
        Assert.Equal(ApprovalTemplateStore.RequestTypes.Count, self.Length + supervisor.Length);
        Assert.Contains(self, t => t.Key == "LeaveRequest");
        Assert.Contains(supervisor, t => t.Key == "Onboarding");
    }
}
