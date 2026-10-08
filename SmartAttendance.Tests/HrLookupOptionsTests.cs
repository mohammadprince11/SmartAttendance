using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public class HrLookupOptionsTests
{
    private static HrLookups.LookupItem Item(string name, bool active = true) =>
        new() { ArabicName = name, IsActive = active };

    [Fact]
    public void ReferenceNationalityUsesExistingStorageValueAndArabicLabel()
    {
        var options = HrLookupOptions.BuildNationalities([Item("عراقي")]);
        Assert.Equal(new HrLookupOptions.Option("Iraqi", "عراقي"), Assert.Single(options));
    }

    [Fact]
    public void CustomNationalityIsNotConvertedToOther()
    {
        var options = HrLookupOptions.BuildNationalities([Item("جنسية تجريبية")]);
        Assert.Equal("جنسية تجريبية", Assert.Single(options).Value);
        Assert.Equal("جنسية تجريبية", HrLookupOptions.PreserveNationality(" جنسية تجريبية "));
    }

    [Fact]
    public void InactiveChoicesAreExcludedExceptExactCurrentValue()
    {
        var options = HrLookupOptions.BuildNationalities([Item("عراقي"), Item("سوري", false)], "Syrian");
        Assert.Equal(new[] { "Iraqi", "Syrian" }, options.Select(x => x.Value));
        Assert.DoesNotContain(HrLookupOptions.BuildNationalities([Item("سوري", false)]), x => x.Value == "Syrian");
    }

    [Fact]
    public void CurrentArabicLegacyValueIsPreservedWithoutReplacingIt()
    {
        var options = HrLookupOptions.BuildNationalities([Item("عراقي")], "عراقي");
        Assert.Contains(options, x => x.Value == "عراقي");
        Assert.Equal("عراقي", HrLookupOptions.PreserveNationality("عراقي"));
    }

    [Fact]
    public void EmptyReferenceListDoesNotRestoreHardcodedChoices()
    {
        Assert.Empty(HrLookupOptions.BuildNationalities([]));
        Assert.Equal("Legacy", Assert.Single(HrLookupOptions.BuildNationalities([], "Legacy")).Value);
    }

    [Fact]
    public void NationalityConditionsSupportBothExistingStorageAliases()
    {
        var choices = HrLookupOptions.BuildConditionOptions("nationalities", [Item("عراقي"), Item("سوري", false)]);
        Assert.Equal(new[] { "عراقي", "Iraqi" }, choices.Select(x => x.Value));
    }

    [Theory]
    [InlineData("religion", "religions")]
    [InlineData("nationality", "nationalities")]
    [InlineData("contracttype", "contracttypes")]
    [InlineData("worktype", "worktypes")]
    [InlineData("jobgrade", "grades")]
    [InlineData("sponsor", "sponsors")]
    public void ConditionCriteriaResolveToTheirReferenceCategory(string criterion, string category) =>
        Assert.Equal(category, HrLookupOptions.ConditionCategories[criterion]);

    [Fact]
    public void NonNationalityConditionsKeepArabicValuesAndSkipInactiveRows()
    {
        var choices = HrLookupOptions.BuildConditionOptions("grades", [Item("درجة تجريبية"), Item("معطلة", false)]);
        Assert.Equal(new HrLookupOptions.Option("درجة تجريبية", "درجة تجريبية"), Assert.Single(choices));
    }
}
