using SmartAttendance.Web.Infrastructure.Notifications;

namespace SmartAttendance.Tests;

public sealed class NotificationEventSourcesTests
{
    [Theory]
    [InlineData("اقتراحات وشكاوي", NotificationRuleGenerator.RuleKind.Feedback)]
    [InlineData("الرد على الاقتراحات والشكاوي", NotificationRuleGenerator.RuleKind.FeedbackReply)]
    [InlineData("تجديد عقد", NotificationRuleGenerator.RuleKind.ContractRenewal)]
    [InlineData("تمديد العقد", NotificationRuleGenerator.RuleKind.ContractExtension)]
    [InlineData("الترحيب بموظف جديد", NotificationRuleGenerator.RuleKind.Welcome)]
    [InlineData("إعادة تعيين الموظف", NotificationRuleGenerator.RuleKind.Rehire)]
    [InlineData("مخالفات الموظفين", NotificationRuleGenerator.RuleKind.Violation)]
    [InlineData("الإجراءات التأديبية المتخذة", NotificationRuleGenerator.RuleKind.DisciplinaryAction)]
    [InlineData("رضا الموظفين", NotificationRuleGenerator.RuleKind.Satisfaction)]
    [InlineData("مقابلات نهاية الخدمة", NotificationRuleGenerator.RuleKind.ExitInterview)]
    [InlineData("وداع موظف", NotificationRuleGenerator.RuleKind.Farewell)]
    public void Supported_operations_have_scoped_parameterized_sources(string name, NotificationRuleGenerator.RuleKind kind)
    {
        Assert.Equal(kind, NotificationRuleGenerator.MapRuleName(name));
        var source = Assert.IsType<NotificationEventSources.Source>(NotificationEventSources.Describe(kind));
        var query = NotificationEventSources.ScopedQuery(source);
        Assert.Contains("employee.CompanyId = @CompanyId", query);
        Assert.Contains("employee.IsDeleted = 0", query);
        if (kind is NotificationRuleGenerator.RuleKind.ExitInterview or NotificationRuleGenerator.RuleKind.Farewell) Assert.DoesNotContain("employee.IsActive = 1", query);
        else Assert.Contains("employee.IsActive = 1", query);
        Assert.Contains("source.OccurredAt >= @EnabledSince", query);
        Assert.Contains("source.OccurredAt <= @Now", query);
        Assert.Contains("NOT EXISTS", query);
        Assert.Contains("@Kind", query);
    }

    [Fact]
    public void Rejected_candidate_is_not_request_rejection()
    {
        Assert.Null(NotificationRuleGenerator.MapRuleName("رفض الموظفين"));
    }

    [Fact]
    public void Source_identity_is_stable_and_distinguishes_company_rule_employee_and_reply_revision()
    {
        var source = new NotificationEventSources.Event(11, 22, new DateTime(2026, 10, 7, 12, 0, 0).AddTicks(1234567));
        var key = NotificationEventSources.Key(NotificationRuleGenerator.RuleKind.FeedbackReply, 3, source);
        Assert.Equal(key, NotificationEventSources.Key(NotificationRuleGenerator.RuleKind.FeedbackReply, 3, source));
        Assert.NotEqual(key, NotificationEventSources.Key(NotificationRuleGenerator.RuleKind.FeedbackReply, 4, source));
        Assert.NotEqual(key, NotificationEventSources.Key(NotificationRuleGenerator.RuleKind.Feedback, 3, source));
        Assert.NotEqual(key, NotificationEventSources.Key(NotificationRuleGenerator.RuleKind.FeedbackReply, 3, source with { EmployeeId = 23 }));
        Assert.NotEqual(key, NotificationEventSources.Key(NotificationRuleGenerator.RuleKind.FeedbackReply, 3, source with { OccurredAt = source.OccurredAt.AddTicks(1) }));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2026, 10, 7)]
    [InlineData(9999, 12, 31)]
    public void Sql_tick_expression_matches_dotnet_without_nanosecond_overflow(int year, int month, int day)
    {
        var timestamp = new DateTime(year, month, day, 23, 59, 59).AddTicks(9999999);
        var days = (long)(timestamp.Date - DateTime.MinValue).TotalDays;
        var dayNanoseconds = timestamp.TimeOfDay.Ticks * 100;
        Assert.Equal(timestamp.Ticks, days * 864000000000 + dayNanoseconds / 100);
    }

    [Fact]
    public void Calendar_rules_do_not_use_operation_sources()
    {
        Assert.Null(NotificationEventSources.Describe(NotificationRuleGenerator.RuleKind.Birthday));
        Assert.Null(NotificationEventSources.Describe(NotificationRuleGenerator.RuleKind.ProbationEnding));
    }
}
