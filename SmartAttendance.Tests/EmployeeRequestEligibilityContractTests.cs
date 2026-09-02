using Xunit;

namespace SmartAttendance.Tests;

public sealed class EmployeeRequestEligibilityContractTests
{
    [Theory]
    [InlineData("Pages/EmployeePortal/MissingPunch.cshtml.cs")]
    [InlineData("Pages/EmployeePortal/DataChange.cshtml.cs")]
    [InlineData("Pages/EmployeePortal/FinancialRequest.cshtml.cs")]
    [InlineData("Pages/EmployeePortal/ShiftRequest.cshtml.cs")]
    [InlineData("Pages/EmployeePortal/DocumentRequest.cshtml.cs")]
    [InlineData("Pages/EmployeePortal/FormFill.cshtml.cs")]
    [InlineData("Pages/MyProfile/Index.cshtml.cs")]
    [InlineData("Pages/SelfServices/Index.cshtml.cs")]
    [InlineData("Pages/MissingPunchRequests/Index.cshtml.cs")]
    [InlineData("Pages/Payroll/FinancialRequests.cshtml.cs")]
    public void RequestCreationPaths_EnforceTheCentralProfileGuard(string relativePath)
    {
        var source = ReadWeb(relativePath);

        Assert.Contains("EmployeeRequestEligibility.CheckAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeePortalHub_EnforcesGuardForEveryCreationHandler()
    {
        var source = ReadWeb("Pages/EmployeePortal/Index.cshtml.cs");

        Assert.Contains("OnGetRequestEligibilityAsync", source, StringComparison.Ordinal);
        Assert.True(
            Count(source, "EmployeeRequestEligibility.CheckAsync") >= 7,
            "بوابة الموظف يجب أن تفحص إنشاء الطلب، الإجازة، البصمة، تعديل البيانات، الشكوى وإعادة الإرسال إضافةً إلى endpoint الواجهة.");
    }

    [Fact]
    public void EmployeePortal_PreventsOpeningAndSubmittingBeforeEligibilityCheck()
    {
        var script = ReadWeb("wwwroot/js/nxex-bottom-nav.js");
        var layout = ReadWeb("Pages/Shared/_EmployeePortalLayout.cshtml");
        var page = ReadWeb("Pages/EmployeePortal/Index.cshtml");

        Assert.Contains("handler=RequestEligibility", script, StringComparison.Ordinal);
        Assert.Contains("hasCompleteRequestProfile", script, StringComparison.Ordinal);
        Assert.Contains("form[data-request-submit]", script, StringComparison.Ordinal);
        Assert.Contains("data-request-eligibility-message", layout, StringComparison.Ordinal);
        Assert.Contains("data-request-entry", layout, StringComparison.Ordinal);
        Assert.Contains("data-request-submit", page, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeePortal_RequestForms_BlockIncompleteClientDataBeforePosting()
    {
        var script = ReadWeb("wwwroot/js/nxex-bottom-nav.js");
        var hub = ReadWeb("Pages/EmployeePortal/Index.cshtml");
        var missingPunch = ReadWeb("Pages/EmployeePortal/MissingPunch.cshtml");

        Assert.Contains("collectMissingRequestFields", script, StringComparison.Ordinal);
        Assert.Contains("syncRequestFormState", script, StringComparison.Ordinal);
        Assert.Contains("button.disabled = blocked", script, StringComparison.Ordinal);
        Assert.Contains("data-request-validation", script, StringComparison.Ordinal);
        Assert.Contains("data-request-required-label=\"تاريخ البداية\"", hub, StringComparison.Ordinal);
        Assert.Contains("data-request-required-label=\"وقت البصمة المفقودة\"", missingPunch, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Pages/EmployeePortal/DataChange.cshtml", "data-request-requires-change")]
    [InlineData("Pages/EmployeePortal/DocumentRequest.cshtml", "data-document-request")]
    [InlineData("Pages/EmployeePortal/FormFill.cshtml", "required=\"@(field.IsRequired")]
    public void EmployeePortal_SpecialRequestForms_DeclareTheirClientValidationContract(
        string relativePath,
        string marker)
    {
        var source = ReadWeb(relativePath);

        Assert.Contains(marker, source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeePortal_DoesNotRewriteConsumedTempDataMessage()
    {
        var source = ReadWeb("Pages/EmployeePortal/Index.cshtml.cs");

        Assert.Contains("_ => null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ => StatusMessage", source, StringComparison.Ordinal);
        Assert.Contains("InlineRequestError", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Pages/EmployeePortal/Index.cshtml.cs", "EmployeePortal.Index.StatusMessage")]
    [InlineData("Pages/EmployeePortal/MissingPunch.cshtml.cs", "EmployeePortal.MissingPunch.StatusMessage")]
    [InlineData("Pages/EmployeePortal/DataChange.cshtml.cs", "EmployeePortal.DataChange.StatusMessage")]
    [InlineData("Pages/EmployeePortal/FinancialRequest.cshtml.cs", "EmployeePortal.FinancialRequest.StatusMessage")]
    [InlineData("Pages/EmployeePortal/ShiftRequest.cshtml.cs", "EmployeePortal.ShiftRequest.StatusMessage")]
    [InlineData("Pages/EmployeePortal/DocumentRequest.cshtml.cs", "EmployeePortal.DocumentRequest.StatusMessage")]
    [InlineData("Pages/EmployeePortal/FormFill.cshtml.cs", "EmployeePortal.FormFill.StatusMessage")]
    public void EmployeePortal_TempDataMessagesAreNamespacedPerPage(string relativePath, string key)
    {
        var source = ReadWeb(relativePath);

        Assert.Contains($"TempData(Key = \"{key}\")", source, StringComparison.Ordinal);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string ReadWeb(string relativePath)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;

        var root = Assert.IsType<DirectoryInfo>(directory).FullName;
        return File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
