using SmartAttendance.Web.Infrastructure.Notifications;

namespace SmartAttendance.Tests;

public sealed class NotificationRoutingPolicyTests
{
    private static readonly NotificationRoutingPolicy.Supervisor[] Supervisors =
        [new(10, "hr.one"), new(11, "hr.two"), new(null, "admin"), new(1, "self")];
    private static readonly NotificationRoutingPolicy.Supervisor[] Users =
        [.. Supervisors, new(20, "manager")];

    [Fact]
    public void EmployeeAudience_OnlySubject_NoHrBroadcastOrSelfRemoval()
    {
        var plan = Resolve("الموظف", "hr.one");
        Assert.Equal([1], plan.EmployeeIds);
        Assert.Empty(plan.BackOfficeUsers);
    }

    [Fact]
    public void SupervisorsAudience_DoesNotAddManagerOrSubject()
    {
        var plan = Resolve("المشرفين");
        Assert.Equal([10, 11], plan.EmployeeIds.Order());
        Assert.Equal(["admin", "hr.one", "hr.two"], plan.BackOfficeUsers.Order());
    }

    [Fact]
    public void SelectedSupervisor_IsAFilter_NotAnExtraRecipient()
    {
        var plan = Resolve("المشرفين", " HR.ONE ");
        Assert.Equal([10], plan.EmployeeIds);
        Assert.Equal(["hr.one"], plan.BackOfficeUsers);
    }

    [Fact]
    public void UnknownSupervisor_DoesNotFallBackToBroadcast()
    {
        var plan = Resolve("المشرفين", "missing");
        Assert.Empty(plan.EmployeeIds);
        Assert.Empty(plan.BackOfficeUsers);
    }

    [Fact]
    public void ManagerAndSupervisors_IncludesManagerAccount()
    {
        var plan = Resolve("المدير المباشر والمشرفون", "hr.one");
        Assert.Equal([10, 20], plan.EmployeeIds.Order());
        Assert.Equal(["hr.one", "manager"], plan.BackOfficeUsers.Order());
    }

    [Fact]
    public void AmbiguousCrossTenantUsername_DoesNotBroadcastButKeepsScopedEmployeeInbox()
    {
        var plan = NotificationRoutingPolicy.Resolve("المشرفين", 1, null,
            [new(10, "same.username", false)], [], "same.username");
        Assert.Equal([10], plan.EmployeeIds);
        Assert.Empty(plan.BackOfficeUsers);
    }

    [Fact]
    public void UnknownAudience_FailsClosed()
    {
        var plan = Resolve("unknown");
        Assert.Empty(plan.EmployeeIds);
        Assert.Empty(plan.BackOfficeUsers);
    }

    [Fact]
    public void NoSupervisors_DoesNotSilentlyRedirectToManager()
    {
        var plan = NotificationRoutingPolicy.Resolve("المشرفين", 1, 20, [], Users, null);
        Assert.Empty(plan.EmployeeIds);
        Assert.Empty(plan.BackOfficeUsers);
    }

    [Fact]
    public void DuplicateRecipients_AreDeduplicated()
    {
        var plan = NotificationRoutingPolicy.Resolve("المدير المباشر والمشرفون", 1, 10,
            [.. Supervisors, new(10, "HR.ONE")], Users, null);
        Assert.Equal(2, plan.EmployeeIds.Count);
        Assert.Equal(3, plan.BackOfficeUsers.Count);
    }

    [Theory]
    [InlineData("كل الموظفين", true)]
    [InlineData("كل العناصر المختارة", true)]
    [InlineData("كل الوثائق", false)]
    [InlineData("كل الفروع", false)]
    [InlineData("", false)]
    public void EmployeeRule_RejectsUnimplementedItemKinds(string value, bool valid) =>
        Assert.Equal(valid, NotificationRoutingPolicy.ValidEmployeeItems(value));

    [Theory]
    [InlineData(2026, 12, 29, 1990, 1, 2, 2027, 1, 2)]
    [InlineData(2027, 2, 20, 2000, 2, 29, 2027, 2, 28)]
    [InlineData(2028, 2, 20, 2000, 2, 29, 2028, 2, 29)]
    [InlineData(2026, 10, 7, 2000, 10, 7, 2026, 10, 7)]
    [InlineData(2026, 10, 8, 2000, 10, 7, 2027, 10, 7)]
    public void AnnualReminder_UsesUpcomingOccurrence(int y, int m, int d, int by, int bm, int bd, int ey, int em, int ed)
    {
        var today = new DateOnly(y, m, d);
        var target = NotificationRoutingPolicy.NextAnnualDate(new(by, bm, bd), today);
        Assert.Equal(new DateOnly(ey, em, ed), target);
        var days = target!.Value.DayNumber - today.DayNumber;
        Assert.True(NotificationRuleGenerator.WithinWindow(target, today, days));
        if (days > 0) Assert.False(NotificationRuleGenerator.WithinWindow(target, today, days - 1));
    }

    [Fact]
    public void MissingOrFutureAnnualDate_DoesNotCreateEvent()
    {
        Assert.Null(NotificationRoutingPolicy.NextAnnualDate(null, new(2026, 10, 7)));
        Assert.Null(NotificationRoutingPolicy.NextAnnualDate(new(2027, 1, 1), new(2026, 10, 7)));
    }

    private static NotificationRoutingPolicy.Plan Resolve(string audience, string? selected = null) =>
        NotificationRoutingPolicy.Resolve(audience, 1, 20, Supervisors, Users, selected);
}
