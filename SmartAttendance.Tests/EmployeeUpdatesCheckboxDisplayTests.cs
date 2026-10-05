using SmartAttendance.Web.Pages.EmployeeUpdates;

namespace SmartAttendance.Tests;

public class EmployeeUpdatesCheckboxDisplayTests
{
    public static IEnumerable<object[]> CheckboxValues() => IndexModel.ProfileFieldDefinitions()
        .Where(field => field.InputType == "checkbox")
        .SelectMany(field => new[] {
            new object[] { field.Key, "true", "نعم" },
            new object[] { field.Key, "false", "لا" },
            new object[] { field.Key, "TRUE", "نعم" },
            new object[] { field.Key, "", "-" }
        });

    [Theory]
    [MemberData(nameof(CheckboxValues))]
    public void BooleanFields_AreDisplayedAsArabicWithoutChangingTheirValue(string key, string value, string expected)
    {
        var model = new IndexModel(null!, null!, null!);
        Assert.Equal(expected, model.FieldDisplayValue(key, value));
    }

    [Theory]
    [InlineData("IsActive", "true", "فعال")]
    [InlineData("IsActive", "false", "غير فعال")]
    [InlineData("BankName", "false", "false")]
    public void OtherFields_RetainTheirOwnDisplaySemantics(string key, string value, string expected)
    {
        var model = new IndexModel(null!, null!, null!);
        Assert.Equal(expected, model.FieldDisplayValue(key, value));
    }
}
