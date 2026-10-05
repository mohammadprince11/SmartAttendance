using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Routing;
using SmartAttendance.Web.Pages.Platform.Tenants;

namespace SmartAttendance.Tests;

public sealed class PlatformPlanCodeValidationTests
{
    [Theory]
    [InlineData("Custom", true)]
    [InlineData("Trial", true)]
    [InlineData("AB", true)]
    [InlineData("A", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PlanCode_ValidatesActualCode(string? code, bool valid)
    {
        var results = new List<ValidationResult>();
        var model = new DetailsModel.InputModel { PlanCode = code! };
        var context = new ValidationContext(model) { MemberName = nameof(model.PlanCode) };
        Assert.Equal(valid, Validator.TryValidateProperty(code, context, results));
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void PlanCode_KeepsLengthBoundary(int length, bool valid) =>
        PlanCode_ValidatesActualCode(new string('A', length), valid);

    [Fact]
    public void ClientRules_UseStringRegex_NotSelectedOptionCount()
    {
        var property = typeof(DetailsModel.InputModel).GetProperty(nameof(DetailsModel.InputModel.PlanCode))!;
        var attributes = property.GetCustomAttributes(typeof(ValidationAttribute), true).Cast<ValidationAttribute>();
        var rules = new Dictionary<string, string>();
        var metadataProvider = new EmptyModelMetadataProvider();
        var metadata = metadataProvider.GetMetadataForType(typeof(string));
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var context = new ClientModelValidationContext(actionContext, metadata, metadataProvider, rules);
        var adapters = new ValidationAttributeAdapterProvider();
        foreach (var attribute in attributes)
            adapters.GetAttributeAdapter(attribute, null)!.AddValidation(context);

        Assert.Equal(@"^[\s\S]{2,60}$", rules["data-val-regex-pattern"]);
        Assert.Contains("data-val-required", rules.Keys);
        Assert.DoesNotContain(rules.Keys, key => key.StartsWith("data-val-length") || key.StartsWith("data-val-minlength"));
    }
}
