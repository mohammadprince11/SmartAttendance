using SmartAttendance.Web.Infrastructure.Notifications;

namespace SmartAttendance.Tests;

public sealed class NotificationRuleCatalogTests
{
    [Theory]
    [InlineData("عيد ميلاد موظف", 13)]
    [InlineData("فترة التجربة", 8)]
    [InlineData("تجديد عقد", 7)]
    [InlineData("تمديد العقد", 8)]
    [InlineData("العطل", 3)]
    [InlineData("مقابلات نهاية الخدمة", 1)]
    [InlineData("رضا الموظفين", 1)]
    [InlineData("اقتراحات وشكاوي", 3)]
    [InlineData("مشاركة التقارير", 2)]
    [InlineData("مشاركة الوثائق", 2)]
    [InlineData("الإنتخابات و إستطلاعات الرأي", 2)]
    public void Per_rule_options_match_observed_reference(string name, int count) => Assert.Equal(count, NotificationRuleCatalog.Audiences(name).Count);

    [Theory]
    [InlineData("عيد ميلاد موظف", NotificationRoutingPolicy.AllEmployees, true)]
    [InlineData("الترحيب بموظف جديد", NotificationRoutingPolicy.Specific, true)]
    [InlineData("فترة التجربة", NotificationRoutingPolicy.AllEmployees, false)]
    [InlineData("تجديد عقد", NotificationRoutingPolicy.Group, false)]
    [InlineData("تمديد العقد", NotificationRoutingPolicy.Group, true)]
    [InlineData("رضا الموظفين", NotificationRoutingPolicy.Self, false)]
    [InlineData("مقابلات نهاية الخدمة", NotificationRoutingPolicy.Supervisors, true)]
    [InlineData("عيد ميلاد موظف", NotificationRuleCatalog.SameHierarchy, false)]
    public void Backend_rejects_wrong_or_unimplemented_audience(string name, string audience, bool allowed) =>
        Assert.Equal(allowed, NotificationRuleCatalog.AllowsAudience(name, audience));

    [Fact]
    public void Announcement_has_no_days_field_and_specific_selection_does_not_broadcast()
    {
        Assert.False(NotificationRuleCatalog.HasDays("عيد ميلاد موظف"));
        Assert.False(NotificationRuleCatalog.AllowsSettings("عيد ميلاد موظف", new() { DaysBefore = 2 }));
        var plan = NotificationRoutingPolicy.Resolve(NotificationRoutingPolicy.Specific, 1, 2, [new(3, "hr")], [], null, [4, 4, 5]);
        Assert.Equal([4, 5], plan.EmployeeIds.Order());
        Assert.Empty(plan.BackOfficeUsers);
        Assert.False(NotificationRuleSettings.Valid(new() { Audience = NotificationRoutingPolicy.Specific }));
    }

    [Fact]
    public void All_employees_routes_only_the_scoped_ids_supplied_by_generator()
    {
        var plan = NotificationRoutingPolicy.Resolve(NotificationRoutingPolicy.AllEmployees, 1, 2, [new(3, "hr")], [], null, [1, 4]);
        Assert.Equal([1, 4], plan.EmployeeIds.Order());
        Assert.Empty(plan.BackOfficeUsers);
    }

    [Theory]
    [InlineData("CreatedAt", "CreatedAt")]
    [InlineData("JoiningDate", "JoiningDate")]
    [InlineData("HireDate", "HireDate")]
    [InlineData("ContractStart", "contract.FromDate")]
    public void Welcome_basis_uses_closed_source_and_business_dates_convert_to_utc(string basis, string column)
    {
        var source = Assert.IsType<NotificationEventSources.Source>(NotificationEventSources.Describe(NotificationRuleGenerator.RuleKind.Welcome, basis));
        Assert.Contains(column, source.Query);
        if (basis != "CreatedAt") Assert.Contains("AT TIME ZONE 'UTC'", source.Query);
        Assert.Null(NotificationEventSources.Describe(NotificationRuleGenerator.RuleKind.Welcome, "invalid; SQL"));
        Assert.False(NotificationRuleSettings.Valid(new() { WelcomeBasis = "invalid; SQL" }));
    }

    [Fact]
    public void Exit_interview_is_explicit_submission_including_departed_subject_but_scoped()
    {
        var source = NotificationEventSources.Describe(NotificationRuleGenerator.RuleKind.ExitInterview)!;
        Assert.Contains("FormType = N'ExitInterview'", source.Query);
        Assert.Contains("Status <> N'Cancelled'", source.Query);
        Assert.True(source.AllowsDepartedSubject);
        Assert.Contains("employee.CompanyId = @CompanyId", NotificationEventSources.ScopedQuery(source));
        Assert.False(NotificationEventSources.Describe(NotificationRuleGenerator.RuleKind.Satisfaction)!.AllowsDepartedSubject);
    }
}
