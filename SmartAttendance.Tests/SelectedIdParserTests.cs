using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public sealed class SelectedIdParserTests
{
    [Fact]
    public void Parse_KeepsDistinctPositiveIds_AndRejectsMalformedValues()
    {
        var values = new string?[] { "12", null, "", "abc", "-4", "0", "12", " 7 " };

        var result = SelectedIdParser.Parse(values);

        Assert.Equal(new[] { 12, 7 }, result);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyList()
    {
        Assert.Empty(SelectedIdParser.Parse(Array.Empty<string?>()));
    }
}
