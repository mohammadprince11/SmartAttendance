using SmartAttendance.Application.Employees.ViewModels;
using SmartAttendance.Web.Infrastructure.Hrms;
using SmartAttendance.Web.Pages.Employees;

namespace SmartAttendance.Tests;

public sealed class EmployeeFormParityTests
{
    [Fact]
    public void CreateEmployeeModel_IsCanonicalAcrossEditAndSmartOnboarding()
    {
        var createProperties = typeof(EmployeeCreateViewModel)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var editProperties = typeof(EmployeeEditViewModel)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var onboardingProperties =
            typeof(SmartOnboardingReviewModel.FinalizeEmployeeInput)
                .GetProperties()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);

        foreach (var fieldName in createProperties)
        {
            Assert.Contains(fieldName, editProperties);
            Assert.Contains(fieldName, onboardingProperties);
        }
    }
    [Fact]
    public void AncillaryEmployeeFields_AreExposedAcrossEntryPaths()
    {
        var pageModels = new[]
        {
            typeof(CreateModel),
            typeof(EditModel),
            typeof(SmartOnboardingReviewModel)
        };

        Assert.NotNull(typeof(CreateModel).GetProperty("FamilyNumber"));
        Assert.NotNull(typeof(EditModel).GetProperty("FamilyNumber"));
        Assert.NotNull(
            typeof(SmartOnboardingReviewModel.FinalizeEmployeeInput)
                .GetProperty("FamilyNumber"));

        foreach (var pageModel in pageModels)
        {
            Assert.NotNull(pageModel.GetProperty("BasicSalary"));
            Assert.NotNull(pageModel.GetProperty("EmployeePhoto"));
            Assert.NotNull(pageModel.GetProperty("EmployeeSignature"));
            Assert.NotNull(pageModel.GetProperty("ProfileDynamicSections"));
        }
    }

    [Fact]
    public void NewEmployeeEntryPaths_StartActiveWithoutStatusControls()
    {
        var root = FindRoot();
        var create = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees", "Create.cshtml"));
        var createModel = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees", "Create.cshtml.cs"));
        var onboarding = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees", "SmartOnboardingReview.cshtml"));
        var onboardingModel = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees", "SmartOnboardingReview.cshtml.cs"));

        Assert.DoesNotContain("asp-for=\"Employee.IsActive\"", create, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"Employee.EmploymentStatus\"", create, StringComparison.Ordinal);
        Assert.Contains("Employee.IsActive = true;", createModel, StringComparison.Ordinal);
        Assert.Contains("Employee.EmploymentStatus = \"Active\";", createModel, StringComparison.Ordinal);

        Assert.DoesNotContain("asp-for=\"Finalize.IsActive\"", onboarding, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"Finalize.EmploymentStatus\"", onboarding, StringComparison.Ordinal);
        Assert.Contains("Finalize.IsActive = true;", onboardingModel, StringComparison.Ordinal);
        Assert.Contains("Finalize.EmploymentStatus = \"Active\";", onboardingModel, StringComparison.Ordinal);
    }

    [Fact]
    public void EditUsesSingleCompactActiveCheckboxWithoutCompanyOrEmploymentStatusField()
    {
        var root = FindRoot();
        var edit = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees", "Edit.cshtml"));

        Assert.Contains("nxr-edit-active-check", edit, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"Employee.IsActive\" type=\"checkbox\"", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("@Model.CurrentCompanyName", edit, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"Employee.EmploymentStatus\"", edit, StringComparison.Ordinal);
    }

    [Fact]
    public void EditEmployeeCode_IsIdentityFieldAndLocksAfterPayrollHistory()
    {
        var root = FindRoot();
        var edit = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees", "Edit.cshtml"));
        var editModel = File.ReadAllText(Path.Combine(
            root, "SmartAttendance.Web", "Pages", "Employees", "Edit.cshtml.cs"));

        Assert.DoesNotContain(
            "اختر صورة PNG أو JPG أو WEBP.",
            edit,
            StringComparison.Ordinal);

        Assert.Contains(
            "Model.EmployeeNoLockedByPayroll",
            edit,
            StringComparison.Ordinal);

        Assert.Contains(
            "FROM dbo.PayrollRunLines",
            editModel,
            StringComparison.Ordinal);

        Assert.Contains(
            "لا يمكن تغيير كود الموظف بعد دخوله في دورة راتب.",
            editModel,
            StringComparison.Ordinal);

        var formStart = edit.IndexOf(
            "id=\"employee-edit-form\"",
            StringComparison.Ordinal);
        Assert.True(formStart >= 0);

        var employeeNo = edit.IndexOf(
            "asp-for=\"Employee.EmployeeNo\"",
            formStart,
            StringComparison.Ordinal);
        var nationalId = edit.IndexOf(
            "asp-for=\"Employee.NationalId\"",
            formStart,
            StringComparison.Ordinal);

        Assert.True(employeeNo >= 0 && nationalId > employeeNo);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find SmartAttendance.slnx.");
    }
}
