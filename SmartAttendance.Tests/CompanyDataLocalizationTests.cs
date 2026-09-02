using SmartAttendance.Web.Infrastructure.Localization;

namespace SmartAttendance.Tests;

public sealed class CompanyDataLocalizationTests
{
    [Fact]
    public void Selection_RequiresOneLanguageAndDefaultAmongIt()
    {
        Assert.NotNull(CompanyLanguagePolicy.ValidateSelection("ar-IQ", []));
        Assert.NotNull(CompanyLanguagePolicy.ValidateSelection("ckb-IQ", ["ar-IQ"]));
        Assert.Null(CompanyLanguagePolicy.ValidateSelection("ar-IQ", ["ar-IQ"]));
        Assert.Null(CompanyLanguagePolicy.ValidateSelection("ar-IQ", ["ar-IQ", "en-US"]));
    }

    [Fact]
    public void RequiredValues_ReportEveryMissingLanguageField()
    {
        CompanyLanguageOption[] languages =
        [
            new("ar-IQ", "العربية", "Arabic", "rtl", true, true),
            new("en-US", "English", "English", "ltr", false, true),
            new("ckb-IQ", "کوردی", "Kurdish", "rtl", false, true)
        ];
        var values = new Dictionary<(string CultureCode, string FieldName), string>
        {
            [("ar-IQ", "Name")] = "قسم الموارد البشرية",
            [("en-US", "Name")] = "Human Resources",
            [("ckb-IQ", "Name")] = ""
        };

        var errors = CompanyLanguagePolicy.MissingRequiredValues(languages, ["Name"], values);

        Assert.Single(errors);
        Assert.Contains("کوردی", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_IsNarrowAndCreatesOnlyLocalizationTables()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Infrastructure",
            "Migrations",
            "20260828161000_AddTenantBusinessDataLocalization.cs"));

        Assert.Contains("CompanyLanguages", migration, StringComparison.Ordinal);
        Assert.Contains("LocalizedEntityValues", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("AddColumn", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("Employees\"", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("AttendanceRecords", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeeCreate_UsesOnlyTheSelectedCompanyPrimaryLanguage()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Employees", "Create.cshtml"));
        var model = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Employees", "Create.cshtml.cs"));
        var css = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "wwwroot", "css", "pages", "create-105041754d.css"));

        Assert.Contains("data-language-company", page, StringComparison.Ordinal);
        Assert.Contains("EmployeeNameTranslations[index]", page, StringComparison.Ordinal);
        Assert.Contains("var primaryLanguage = languages.FirstOrDefault(item => item.IsDefault) ?? languages[0]", model, StringComparison.Ordinal);
        Assert.Contains("الاسم الأول مطلوب باللغة الأساسية", model, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var language in languages)", model, StringComparison.Ordinal);
        Assert.Contains("SaveEmployeeNameTranslationsAsync", model, StringComparison.Ordinal);
        Assert.Contains("if (languages.Count == 0)", model, StringComparison.Ordinal);
        Assert.Contains("id=\"SelectedCompanyId\"", page, StringComparison.Ordinal);
        Assert.Contains("NexoraCreateFilterCompany", page, StringComparison.Ordinal);
        Assert.DoesNotContain("تظهر اللغة الأساسية وأي لغات إضافية مفعلة للشركة", page, StringComparison.Ordinal);
        Assert.DoesNotContain(">إعداد اللغات</a>", page, StringComparison.Ordinal);
        Assert.True(
            page.IndexOf("id=\"SelectedCompanyId\"", StringComparison.Ordinal) <
            page.IndexOf("data-employee-multilingual", StringComparison.Ordinal),
            "The company selector must appear before multilingual employee-name fields.");
        Assert.True(
            page.IndexOf("data-employee-multilingual", StringComparison.Ordinal) <
            page.IndexOf("id=\"Employee_BranchId\"", StringComparison.Ordinal),
            "Work location belongs to employment data and must not control whether basic name fields appear.");
        Assert.Contains(".zy-employee-language-company[hidden]{display:none!important}", css, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "SmartAttendance.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
