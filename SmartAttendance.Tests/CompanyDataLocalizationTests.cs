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
    public void EmployeeCreate_UsesEveryActiveCompanyLanguage()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Employees", "Create.cshtml"));
        var model = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Employees", "Create.cshtml.cs"));
        var css = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "wwwroot", "css", "pages", "create-105041754d.css"));

        Assert.Contains("data-language-company", page, StringComparison.Ordinal);
        Assert.Contains("EmployeeNameTranslations[index]", page, StringComparison.Ordinal);
        Assert.Contains("foreach (var language in languages)", model, StringComparison.Ordinal);
        Assert.Contains("ValidateRequiredValuesAsync", model, StringComparison.Ordinal);
        Assert.Contains("data-culture=\"@language.CultureCode\"", page, StringComparison.Ordinal);
        Assert.Contains("SaveEmployeeNameTranslationsAsync", model, StringComparison.Ordinal);
        Assert.Contains("if (languages.Count == 0)", model, StringComparison.Ordinal);
        Assert.Contains("id=\"SelectedCompanyId\"", page, StringComparison.Ordinal);
        Assert.Contains("ZynoraCreateFilterCompany", page, StringComparison.Ordinal);
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

    [Fact]
    public void CompanyCreate_RequiresArabicAndEnglishAndUsesTheCurrentTenant()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Companies", "Create.cshtml"));
        var pageModel = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Companies", "Create.cshtml.cs"));
        var service = File.ReadAllText(Path.Combine(root, "SmartAttendance.Infrastructure", "Services", "CompanyService.cs"));

        Assert.Contains("asp-for=\"ArabicName\"", page, StringComparison.Ordinal);
        Assert.Contains("asp-for=\"EnglishName\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("asp-for=\"Company.Name\"", page, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"مثال: شركة زينورا\"", page, StringComparison.Ordinal);
        Assert.Contains("placeholder=\"Example: Zynora Company\"", page, StringComparison.Ordinal);
        Assert.Contains("ArabicName { get; set; } = string.Empty", pageModel, StringComparison.Ordinal);
        Assert.Contains("EnglishName { get; set; } = string.Empty", pageModel, StringComparison.Ordinal);
        Assert.Contains("BuildLanguage(company.Id, arabic, isDefault: true)", pageModel, StringComparison.Ordinal);
        Assert.Contains("BuildLanguage(company.Id, english, isDefault: false)", pageModel, StringComparison.Ordinal);
        Assert.Contains("BuildCompanyName(company.Id, \"ar-IQ\", ArabicName)", pageModel, StringComparison.Ordinal);
        Assert.Contains("BuildCompanyName(company.Id, \"en-US\", EnglishName)", pageModel, StringComparison.Ordinal);
        Assert.Contains("company.TenantId = tenantId", service, StringComparison.Ordinal);
        Assert.Contains("x.TenantId == tenantId", service, StringComparison.Ordinal);
        Assert.Contains("var isFirstCompany = !await _dbContext.Companies", pageModel, StringComparison.Ordinal);
        Assert.Contains("var continueOnboarding = Onboarding || isFirstCompany", pageModel, StringComparison.Ordinal);
        Assert.Contains("RedirectToPage(\"/Setup/Index\", new { onboarding = true })", pageModel, StringComparison.Ordinal);
    }

    [Fact]
    public void CompanyManagement_IsTenantScopedBeforeListingOrLoadingTranslations()
    {
        var root = FindRepositoryRoot();
        var service = File.ReadAllText(Path.Combine(root, "SmartAttendance.Infrastructure", "Services", "CompanyService.cs"));
        var index = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Companies", "Index.cshtml.cs"));
        var edit = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Companies", "Edit.cshtml.cs"));
        var delete = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Pages", "Companies", "Delete.cshtml.cs"));

        Assert.Contains("GetAllAsync(int tenantId", service, StringComparison.Ordinal);
        Assert.Contains("x.TenantId == tenantId && !x.IsDeleted", service, StringComparison.Ordinal);
        Assert.Contains("GetEditByIdAsync(int id, int tenantId)", service, StringComparison.Ordinal);
        Assert.Contains("UpdateAsync(CompanyEditViewModel model, int tenantId)", service, StringComparison.Ordinal);
        Assert.Contains("DeleteAsync(int id, int tenantId)", service, StringComparison.Ordinal);
        Assert.Contains("GetAllAsync(tenantId.Value, SearchTerm)", index, StringComparison.Ordinal);
        Assert.Contains("GetEditByIdAsync(id, tenantId.Value)", edit, StringComparison.Ordinal);
        Assert.True(
            edit.IndexOf("GetEditByIdAsync(Company.Id, tenantId.Value)", StringComparison.Ordinal) <
            edit.IndexOf("LoadTranslationsAsync(true)", StringComparison.Ordinal),
            "Tenant ownership must be verified before localized company data is loaded.");
        Assert.Contains("GetByIdAsync(id, tenantId.Value)", delete, StringComparison.Ordinal);
        Assert.Contains("DeleteAsync(id, tenantId.Value)", delete, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "SmartAttendance.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
