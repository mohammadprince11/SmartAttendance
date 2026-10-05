using System.Globalization;
using System.Reflection;
using SmartAttendance.Domain.Entities;
using Model = SmartAttendance.Web.Pages.EmployeeUpdates.IndexModel;

namespace SmartAttendance.Tests;

public class EmployeeUpdatesProfileParityTests
{
    public static IEnumerable<object[]> BoundFields() => Model.ProfileFieldDefinitions()
        .Where(f => !f.ReadOnly && f.Target is "employee" or "compensation")
        .Select(f => new object[] { f.Key });

    [Theory]
    [MemberData(nameof(BoundFields))]
    public void EveryEditableScalar_RoundTripsTheProfileEntityProperty(string key)
    {
        var field = Model.ProfileFieldDefinitions().Single(f => f.Key == key);
        var property = Model.ProfileProperty(field);
        Assert.NotNull(property);
        Assert.Equal(field.Target == "employee" ? typeof(Employee) : typeof(EmployeeFinancialInfo), property!.DeclaringType);
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var value = field.Options.Length > 0 ? field.Options.Split('\n')[0] :
            type == typeof(DateOnly) ? "2000-03-25" : type == typeof(bool) ? "true" :
            type == typeof(decimal) ? "125.1234" : type == typeof(int) ? "3" : "Synthetic";
        Assert.True(Model.ValidFieldValue(field, value));
        Assert.True(Model.TryProfileValue(property.PropertyType, value, out var parsed));
        object entity = field.Target == "employee" ? new Employee() : new EmployeeFinancialInfo();
        property.SetValue(entity, parsed);
        Assert.Equal(value, Model.ProfileValue(property.GetValue(entity)));
    }

    [Theory]
    [InlineData("BasicSalary", "not-a-number")]
    [InlineData("BasicSalary", "-1")]
    [InlineData("BasicSalary", "1,000")]
    [InlineData("BirthDate", "2000-02-30")]
    [InlineData("HireDate", "")]
    [InlineData("PositionId", "0")]
    [InlineData("BranchId", "-1")]
    [InlineData("Currency", "FAKE")]
    [InlineData("PaymentMethod", "FAKE")]
    [InlineData("IsCitizen", "yes")]
    [InlineData("IsActive", "yes")]
    public void MalformedInput_IsRejectedBeforeStagingOrApplying(string key, string value)
    {
        Assert.False(Model.ValidFieldValue(Model.ProfileFieldDefinitions().Single(f => f.Key == key), value));
    }

    [Fact]
    public void NullableValues_CanClear_WhileZeroAndFalseAreNotLost()
    {
        Assert.True(Model.TryProfileValue(typeof(decimal?), "", out var cleared));
        Assert.Null(cleared);
        Assert.Equal("0", Model.ProfileValue(0m));
        Assert.Equal("false", Model.ProfileValue(false));
        Assert.False(Model.TryProfileValue(typeof(DateOnly), "", out _));
    }

    [Fact]
    public void DecimalConversion_IsCultureIndependent_AndPreservesFinancialPrecision()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.True(Model.TryProfileValue(typeof(decimal?), "12.1234", out var parsed));
            Assert.Equal(12.1234m, parsed);
            Assert.Equal("12.1234", Model.ProfileValue(parsed));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("FullName")]
    [InlineData("Allowances")]
    [InlineData("Deductions")]
    [InlineData("BankAccount")]
    [InlineData("ShiftName")]
    [InlineData("GraceMinutes")]
    [InlineData("TaxProfileId")]
    public void SpecialWorkflowsAndLegacyFields_CannotWriteWrongTables(string key)
    {
        var field = Model.ProfileFieldDefinitions().Single(f => f.Key == key);
        Assert.True(field.ReadOnly);
        Assert.False(Model.ValidFieldValue(field, "3"));
        Assert.Null(Model.ProfileProperty(field));
    }

    [Fact]
    public void DynamicFields_PreserveChoicesRequiredCheckboxAndFinancialClassification()
    {
        var select = new Model.UpdateField("SyntheticChoice", "Choice", "custom", "select", "", "A\nB", true);
        Assert.True(Model.ValidFieldValue(select, "A"));
        Assert.False(Model.ValidFieldValue(select, "C"));
        Assert.False(Model.ValidFieldValue(select, ""));
        var checkbox = new Model.UpdateField("SyntheticFlag", "Flag", "custom", "checkbox", "");
        Assert.True(Model.ValidFieldValue(checkbox, ""));
        Assert.True(Model.ValidFieldValue(checkbox, "true"));
        Assert.False(Model.ValidFieldValue(checkbox, "maybe"));
        Assert.True(Model.IsFinancialField(select with { Target = "financial-custom" }));
    }

    [Fact]
    public void AddedEmployeeFieldsAndFinancialBankFields_AreCanonicalAndUnique()
    {
        var fields = Model.ProfileFieldDefinitions();
        Assert.Equal(fields.Count, fields.Select(f => f.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var key in Model.AdditionalEmployeeKeys.Append("ContractType").Append("PositionId").Append("BranchId"))
            Assert.Equal(typeof(Employee), Model.ProfileProperty(fields.Single(f => f.Key == key))!.DeclaringType);
        foreach (var key in new[] { "BasicSalary", "Currency", "PaymentMethod", "BankName", "Iban", "CardNo", "MxpAccount" })
            Assert.Equal(typeof(EmployeeFinancialInfo), Model.ProfileProperty(fields.Single(f => f.Key == key))!.DeclaringType);
    }

    [Fact]
    public void PersistedWorkflow_RechecksBeforeValuesAndPermissions_BeforeAnyApplication()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "SmartAttendance.Web", "Pages", "EmployeeUpdates", "Index.cshtml.cs"));
        var start = source.IndexOf("public async Task<IActionResult> OnPostLockAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<IActionResult> OnPostDeleteOpenAsync", start, StringComparison.Ordinal);
        var body = source[start..end];
        Assert.Contains("CanEditProfileAsync", body);
        Assert.Contains("UPDLOCK, HOLDLOCK", body);
        Assert.Contains("IsolationLevel.Serializable", body);
        Assert.Contains("NormalizeValue(change.OldValue)", body);
        Assert.Contains("field.ReadOnly", body);
        Assert.True(body.IndexOf("ValidProfileAssignmentAsync", StringComparison.Ordinal) < body.IndexOf("await ApplyProfileFieldAsync", StringComparison.Ordinal));
        Assert.True(body.IndexOf("NormalizeValue(change.OldValue)", StringComparison.Ordinal) < body.IndexOf("await ApplyProfileFieldAsync", StringComparison.Ordinal));
        Assert.Contains("transaction.CommitAsync", body);
        Assert.DoesNotContain("EmployeeCompensations", source);
        Assert.Contains("!Request.Form.ContainsKey(field.Key)", source);
    }
}
