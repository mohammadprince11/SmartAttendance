using SmartAttendance.Web.Infrastructure.Security;

namespace SmartAttendance.Tests;

public sealed class EndServiceAccessPolicyTests
{
    [Fact]
    public void Optional_hr_notes_are_not_required_by_Mvc_model_binding()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
        Microsoft.Extensions.DependencyInjection.MvcServiceCollectionExtensions.AddMvc(services);
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var metadata = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Microsoft.AspNetCore.Mvc.ModelBinding.IModelMetadataProvider>(provider)
            .GetMetadataForProperties(typeof(SmartAttendance.Web.Pages.Employees.EndServiceModel))
            .Single(property => property.PropertyName == "HrNotes");
        Assert.False(metadata.IsRequired);
    }

    [Theory]
    [InlineData("/Account/Farewell", "GET", true)]
    [InlineData("/Account/Logout", "GET", true)]
    [InlineData("/Account/Farewell", "POST", false)]
    [InlineData("/Account/Farewell/Private", "GET", false)]
    [InlineData("/EmployeePortal", "GET", false)]
    [InlineData("/api/employees", "GET", false)]
    [InlineData("/files/private", "GET", false)]
    public void Grace_is_a_closed_read_only_allowlist(string path, string method, bool allowed) =>
        Assert.Equal(allowed, EndServiceAccessPolicy.IsFarewellOnlyRoute(path, method));

    private static readonly DateOnly LastDay = new(2026, 10, 7);
    private static readonly DateTimeOffset NoonUtc = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cutoff_is_next_Baghdad_midnight_not_server_midnight_or_24_hours_after_save()
    {
        var timing = EndServiceAccessPolicy.Plan(LastDay, false, NoonUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 21, 0, 0, TimeSpan.Zero), timing.AccessEndsAtUtc);
        Assert.Equal(NoonUtc, timing.NotificationEligibleAtUtc);
        Assert.True(timing.CanNotifyInPortal);
    }

    [Fact]
    public void Future_end_notification_waits_for_last_working_day()
    {
        var timing = EndServiceAccessPolicy.Plan(LastDay, false, NoonUtc.AddDays(-2));
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 21, 0, 0, TimeSpan.Zero), timing.NotificationEligibleAtUtc);
        Assert.False(EndServiceAccessPolicy.CanNotifyInPortal(timing, NoonUtc.AddDays(-1)));
        Assert.True(EndServiceAccessPolicy.CanNotifyInPortal(timing, NoonUtc));
    }

    [Fact]
    public void Immediate_end_never_keeps_account_open_for_delivery()
    {
        var timing = EndServiceAccessPolicy.Plan(LastDay.AddDays(30), true, NoonUtc);
        Assert.Equal(NoonUtc, timing.AccessEndsAtUtc);
        Assert.Equal(NoonUtc, timing.NotificationEligibleAtUtc);
        Assert.False(timing.CanNotifyInPortal);
        Assert.False(EndServiceAccessPolicy.AllowsAccess(true, timing.AccessEndsAtUtc, NoonUtc));
    }

    [Fact]
    public void Past_end_date_never_grants_new_access_or_waits_for_notifications()
    {
        var timing = EndServiceAccessPolicy.Plan(LastDay.AddDays(-1), false, NoonUtc);
        Assert.Equal(NoonUtc, timing.AccessEndsAtUtc);
        Assert.False(timing.CanNotifyInPortal);
    }

    [Fact]
    public void Existing_sessions_and_delayed_workers_must_deny_at_exact_deadline()
    {
        var timing = EndServiceAccessPolicy.Plan(LastDay, false, NoonUtc);
        Assert.True(EndServiceAccessPolicy.AllowsAccess(true, timing.AccessEndsAtUtc, timing.AccessEndsAtUtc.AddTicks(-1)));
        Assert.False(EndServiceAccessPolicy.AllowsAccess(true, timing.AccessEndsAtUtc, timing.AccessEndsAtUtc));
        Assert.False(EndServiceAccessPolicy.AllowsAccess(true, timing.AccessEndsAtUtc, timing.AccessEndsAtUtc.AddDays(1)));
        Assert.False(EndServiceAccessPolicy.CanNotifyInPortal(timing, timing.AccessEndsAtUtc));
        Assert.False(EndServiceAccessPolicy.AllowsAccess(false, timing.AccessEndsAtUtc, NoonUtc));
    }

    [Fact]
    public void Input_offsets_and_month_year_boundaries_use_absolute_UTC()
    {
        var date = new DateOnly(2026, 12, 31);
        var now = new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.FromHours(3));
        var timing = EndServiceAccessPolicy.Plan(date, false, now);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 21, 0, 0, TimeSpan.Zero), timing.AccessEndsAtUtc);
        Assert.True(EndServiceAccessPolicy.AllowsAccess(true, timing.AccessEndsAtUtc, now));
        Assert.False(EndServiceAccessPolicy.AllowsAccess(true, timing.AccessEndsAtUtc, timing.AccessEndsAtUtc.ToOffset(TimeSpan.FromHours(3))));
        Assert.Throws<ArgumentOutOfRangeException>(() => EndServiceAccessPolicy.Plan(DateOnly.MaxValue, false, now));
    }
}
