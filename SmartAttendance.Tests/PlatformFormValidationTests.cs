using Microsoft.AspNetCore.Mvc.ModelBinding;
using SmartAttendance.Web.Infrastructure.Platform;

namespace SmartAttendance.Tests;

public sealed class PlatformFormValidationTests
{
    [Fact]
    public void LicenseSave_IgnoresErrorsFromIndependentForms()
    {
        var state = new ModelStateDictionary();
        state.AddModelError("Renewal.VersionToken", "Not submitted");
        state.AddModelError("Domain.CustomDomain", "Not submitted");
        state.AddModelError("CustomerProfile.Name", "Not submitted");
        state.SetModelValue("Input.MaxEmployees", "11", "11");
        state.MarkFieldValid("Input.MaxEmployees");

        PlatformFormValidation.RemoveOtherForms(state, "Renewal", "Domain", "CustomerProfile");

        Assert.True(state.IsValid);
        Assert.Equal("11", state["Input.MaxEmployees"]!.AttemptedValue);
    }

    [Theory]
    [InlineData("Input.MaxEmployees")]
    [InlineData("Input.StartsAt")]
    [InlineData("Input.VersionToken")]
    [InlineData("EnabledModules[0]")]
    [InlineData("")]
    [InlineData("RenewalUnexpected")]
    public void LicenseSave_PreservesSubmittedAndGlobalErrors(string key)
    {
        var state = new ModelStateDictionary();
        state.AddModelError(key, "Invalid submitted value");
        state.AddModelError("Renewal.VersionToken", "Other form");

        PlatformFormValidation.RemoveOtherForms(state, "Renewal", "Domain", "CustomerProfile");

        Assert.False(state.IsValid);
        Assert.Single(state[key]!.Errors);
        Assert.False(state.ContainsKey("Renewal.VersionToken"));
    }
}
