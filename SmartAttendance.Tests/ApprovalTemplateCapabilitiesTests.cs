using SmartAttendance.Web.Infrastructure.Hrms;

namespace SmartAttendance.Tests;

public sealed class ApprovalTemplateCapabilitiesTests
{
    [Fact]
    public void EverySupportedType_HasModuleAndDescription()
    {
        foreach (var type in ApprovalTemplateStore.RequestTypes)
        {
            var definition = Assert.IsType<ApprovalTemplateCapabilities.Definition>(ApprovalTemplateCapabilities.For(type.Key));
            Assert.Contains(definition.Module, ApprovalTemplateCapabilities.Modules);
            Assert.False(string.IsNullOrWhiteSpace(definition.Description));
        }
        Assert.Equal(5, ApprovalTemplateStore.RequestTypes.Select(t => ApprovalTemplateCapabilities.For(t.Key)!.Module).Distinct().Count());
        Assert.All(ApprovalTemplateStore.RequestTypes, t => Assert.True(ApprovalTemplateNavigation.VisibleIn(t.Key, "All")));
        Assert.Null(ApprovalTemplateCapabilities.For("UnsupportedModule"));
    }

    [Fact]
    public void FinancialCatalog_UsesTheSameApprovalKeys()
    {
        foreach (var kind in FinancialRequestStore.Catalog)
        {
            var definition = ApprovalTemplateCapabilities.For(kind.TemplateKey);
            Assert.NotNull(definition);
            Assert.Equal("الرواتب", definition.Module);
            Assert.NotNull(definition.NumericLabel);
        }
    }

    [Theory]
    [InlineData("LeaveRequest", true)]
    [InlineData("Loan", true)]
    [InlineData("FinancialClaim", true)]
    [InlineData("SalaryIncrease", true)]
    [InlineData("ExitPermission", true)]
    [InlineData("MissingPunch", false)]
    [InlineData("Overtime", false)]
    [InlineData("InfoChange", false)]
    [InlineData("DocumentRequest", false)]
    public void NumericConditions_AreLimitedToResolverCapabilities(string type, bool supported)
    {
        var template = new ApprovalTemplateStore.TemplateRow {RequestType=type, HasConditions=true, CondMinAmount=0};
        Assert.Equal(supported, ApprovalTemplateCapabilities.ValidateConditions(template) == null);
    }

    [Theory]
    [InlineData("InfoChange", true)]
    [InlineData("Loan", false)]
    [InlineData("CustomRequest", false)]
    public void ChangedField_IsRestrictedToEmployeeDataChanges(string type, bool supported)
    {
        var template = new ApprovalTemplateStore.TemplateRow {RequestType=type, HasConditions=true, CondChangedFieldKey="WorkType"};
        Assert.Equal(supported, ApprovalTemplateCapabilities.ValidateConditions(template) == null);
    }

    [Fact]
    public void DisabledConditions_DoNotAffectValidation()
        => Assert.Null(ApprovalTemplateCapabilities.ValidateConditions(new() {RequestType="Overtime", HasConditions=false, CondMinAmount=1, CondChangedFieldKey="WorkType"}));
}
