using SmartAttendance.Web.Infrastructure.Notifications;

namespace SmartAttendance.Tests;

public sealed class NotificationRuleSettingsTests
{
    [Theory]
    [InlineData("{{EmployeeName}}", true)]
    [InlineData("{{RuleName}} — {{Date}}", true)]
    [InlineData("{{Salary}}", false)]
    [InlineData("{{EmployeeName", false)]
    [InlineData("{{{{EmployeeName}}}}", false)]
    [InlineData("{{Unknown}}", false)]
    [InlineData("", true)]
    public void Template_AllowsOnlyKnownBalancedTokens(string value, bool expected) =>
        Assert.Equal(expected, NotificationTemplate.Valid(value, 200));

    [Fact]
    public void Template_ExpandsOnce_NotRecursively()
    {
        var result = NotificationTemplate.Render("{{EmployeeName}} / {{RuleName}} / {{Date}} / {{Message}}", "fallback",
            "{{RuleName}}", "قاعدة تجريبية", new(2026, 10, 7), "رسالة");
        Assert.Equal("{{RuleName}} / قاعدة تجريبية / 2026-10-07 / رسالة", result);
    }

    [Fact]
    public void BlankTemplate_PreservesOriginalMessage() =>
        Assert.Equal("original", NotificationTemplate.Render(" ", "original", "employee", "rule", new(2026, 10, 7), "message"));

    [Fact]
    public void Channels_CannotBothBeDisabled() =>
        Assert.False(NotificationRuleSettings.Valid(new() { InApp = false, Email = false }));

    [Fact]
    public void Group_MustHaveUniquePositiveMembers()
    {
        var settings = new NotificationRuleSettings { Audience = NotificationRoutingPolicy.Group };
        Assert.False(NotificationRuleSettings.Valid(settings));
        Assert.False(NotificationRuleSettings.Valid(settings with { GroupMembers = [1, 1] }));
        Assert.False(NotificationRuleSettings.Valid(settings with { GroupMembers = [0] }));
        Assert.True(NotificationRuleSettings.Valid(settings with { GroupMembers = [1, 2] }));
    }

    [Fact]
    public void CompanyKeys_DoNotOverlap() =>
        Assert.NotEqual(NotificationRuleSettings.Key(1, 10), NotificationRuleSettings.Key(2, 10));

    [Theory]
    [InlineData("الموظف نفسه", "1")]
    [InlineData("المدير المباشر", "20")]
    [InlineData("الموظف نفسه و مديره المباشر", "1,20")]
    [InlineData("الموظف نفسه والمشرفون", "1,10")]
    [InlineData("الموظف نفسه و مديره المباشر والمشرفون", "1,10,20")]
    [InlineData("مجموعة الموظفين", "30,40")]
    public void Audience_RoutesExactlySelectedCombination(string audience, string expected)
    {
        var result = NotificationRoutingPolicy.Resolve(audience, 1, 20, [new(10, "hr")], [new(20, "manager")], null, [30, 40]);
        Assert.Equal(expected, string.Join(",", result.EmployeeIds.Order()));
    }

    [Fact]
    public void Reference_HasEightAudienceChoices() => Assert.Equal(8, NotificationRoutingPolicy.Audiences.Count);

    [Theory]
    [InlineData("رفض الموظفين")]
    [InlineData("إنشاء صلاحية الوثيقة")]
    public void SemanticallyChangedLegacyFlags_DoNotActivateNewMeaning(string name) =>
        Assert.False(NotificationRuleSettings.FromLegacy(new() { Name = name, IsEnabled = true }).IsEnabled);

    [Fact]
    public void Satisfaction_IsExplicitSurveyType_NotAnyForm() =>
        Assert.True(SmartAttendance.Web.Infrastructure.Hrms.FormBuilder.IsSurveyLike("Satisfaction"));

    [Fact]
    public void Retirement_UsesConfiguredAge_WithoutInventedDefault()
    {
        Assert.Null(NotificationRuleGenerator.RetirementDate(new(1980, 1, 1), null));
        Assert.Null(NotificationRuleGenerator.RetirementDate(null, 60));
        Assert.Null(NotificationRuleGenerator.RetirementDate(new(1980, 1, 1), 0));
        Assert.Equal(new DateOnly(2040, 2, 29), NotificationRuleGenerator.RetirementDate(new(1980, 2, 29), 60));
        Assert.Equal(new DateOnly(2039, 2, 28), NotificationRuleGenerator.RetirementDate(new(1980, 2, 29), 59));
    }
}
