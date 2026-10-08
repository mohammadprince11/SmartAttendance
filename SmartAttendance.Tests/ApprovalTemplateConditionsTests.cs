using System.Text.Json;
using SmartAttendance.Web.Infrastructure.Hrms;
using Condition = SmartAttendance.Web.Infrastructure.Hrms.ApprovalTemplateConditions.Condition;

namespace SmartAttendance.Tests;

public sealed class ApprovalTemplateConditionsTests
{
    private static string Json(params ApprovalTemplateConditions.Condition[] conditions) => JsonSerializer.Serialize(conditions);

    [Theory]
    [InlineData("eq", 2, true)] [InlineData("eq", 3, false)]
    [InlineData("ne", 2, false)] [InlineData("ne", 3, true)]
    [InlineData("gt", 2, false)] [InlineData("gt", 3, true)]
    [InlineData("ge", 2, true)] [InlineData("ge", 1, false)]
    [InlineData("lt", 1, true)] [InlineData("lt", 2, false)]
    [InlineData("le", 2, true)] [InlineData("le", 3, false)]
    public void NumericOperators(string op, int actual, bool expected) => Assert.Equal(expected,
        ApprovalTemplateConditions.Matches("Overtime", Json(new Condition("Hours", op, "2")), new Dictionary<string, decimal?> { ["Hours"] = actual }));

    [Theory]
    [InlineData("eq")] [InlineData("ne")] [InlineData("gt")] [InlineData("ge")] [InlineData("lt")] [InlineData("le")]
    public void MissingValuesNeverMatch(string op) => Assert.False(ApprovalTemplateConditions.Matches("Loan", Json(new Condition("Amount", op, "2")), new Dictionary<string, decimal?>()));

    [Theory]
    [InlineData("Loan", "Hours", "eq", "2")]
    [InlineData("Overtime", "Amount", "eq", "2")]
    [InlineData("LeaveRequest", "DaysCount", "bad", "2")]
    [InlineData("LeaveRequest", "DaysCount", "eq", "-1")]
    [InlineData("LeaveRequest", "FromDate", "eq", "2026-02-30")]
    [InlineData("LeaveRequest", "FromDate", "eq", "10/08/2026")]
    [InlineData("Loan", "InstallmentCount", "eq", "1.5")]
    [InlineData("Loan", "FinancialKind", "gt", "1")]
    [InlineData("Loan", "FinancialKind", "eq", "3")]
    [InlineData("InfoChange", "Amount", "eq", "2")]
    [InlineData("LeaveRequest", "RequestTypeId", "eq", "0")]
    [InlineData("LeaveRequest", "RequestTypeId", "eq", "1.5")]
    [InlineData("LeaveRequest", "RequestTypeId", "gt", "1")]
    public void RejectUnsupportedAndMalformedConditions(string type, string field, string op, string value) =>
        Assert.NotNull(ApprovalTemplateConditions.Validate(type, Json(new Condition(field, op, value))));

    [Theory]
    [InlineData("null")] [InlineData("{}")] [InlineData("[null]")] [InlineData("[")]
    public void InvalidJsonFailsClosed(string json)
    {
        Assert.NotNull(ApprovalTemplateConditions.Validate("Loan", json));
        Assert.False(ApprovalTemplateConditions.Matches("Loan", json, new Dictionary<string, decimal?>()));
    }

    [Fact]
    public void DateAndNumberMustBothMatch()
    {
        var json = Json(new("DaysCount", "ge", "2"), new("FromDate", "ge", "2026-10-08"));
        var values = new Dictionary<string, decimal?> { ["DaysCount"] = 2, ["FromDate"] = new DateOnly(2026, 10, 8).DayNumber };
        Assert.True(ApprovalTemplateConditions.Matches("LeaveRequest", json, values));
        values["DaysCount"] = 1;
        Assert.False(ApprovalTemplateConditions.Matches("LeaveRequest", json, values));
    }

    [Fact]
    public void FinancialKindAndUnitsAreExplicit()
    {
        Assert.True(ApprovalTemplateConditions.Matches("SalaryIncrease", Json(new Condition("RaiseType", "eq", "2")), new Dictionary<string, decimal?> { ["RaiseType"] = 2 }));
        Assert.False(ApprovalTemplateConditions.Matches("SalaryIncrease", Json(new Condition("RaiseType", "eq", "2")), new Dictionary<string, decimal?> { ["RaiseType"] = 1 }));
        Assert.DoesNotContain(ApprovalTemplateConditions.Fields("Overtime"), f => f.Key == "DaysCount");
        Assert.Contains(ApprovalTemplateConditions.Fields("Loan"), f => f.Key == "InstallmentCount");
    }

    [Fact]
    public void LimitsAndEmptyLegacyConditions()
    {
        Assert.Null(ApprovalTemplateConditions.Validate("Loan", null));
        Assert.True(ApprovalTemplateConditions.Matches("Loan", "[]", new Dictionary<string, decimal?>()));
        Assert.NotNull(ApprovalTemplateConditions.Validate("Loan", new string(' ', 16001) + "[]"));
        Assert.NotNull(ApprovalTemplateConditions.Validate("Loan", Json(Enumerable.Repeat(new ApprovalTemplateConditions.Condition("Amount", "ge", "0"), 21).ToArray())));
    }

    [Fact]
    public void StoreValidationCannotBypassTypedValidation()
    {
        var template = new ApprovalTemplateStore.TemplateRow { RequestType = "Loan", Name = "Synthetic", HasConditions = true, ConditionsJson = Json(new Condition("Hours", "gt", "1")) };
        template.Steps.Add(new() { ApproverType = "DirectManager" });
        Assert.NotNull(ApprovalTemplateStore.Validate(template));
        template.ConditionsJson = Json(new Condition("Amount", "gt", "1"));
        Assert.Null(ApprovalTemplateStore.Validate(template));
    }

    [Fact]
    public void MigrationIsAdditiveAndRegisteredOnce()
    {
        var migration = Assert.Single(SqlSchemaMigrator.Migrations, m => m.Id == "20261008-01-approval-typed-conditions");
        Assert.Contains("COL_LENGTH('ApprovalTemplates','ConditionsJson') IS NULL", migration.Sql);
        Assert.DoesNotContain("UPDATE ", migration.Sql);
    }

    [Fact]
    public void RequestTypeUsesStableIdAndDoesNotMatchMissingType()
    {
        var json = Json(new Condition("RequestTypeId", "eq", "10"));
        Assert.True(ApprovalTemplateConditions.Matches("LeaveRequest", json, new Dictionary<string, decimal?> { ["RequestTypeId"] = 10 }));
        Assert.False(ApprovalTemplateConditions.Matches("LeaveRequest", json, new Dictionary<string, decimal?> { ["RequestTypeId"] = null }));
    }
}
