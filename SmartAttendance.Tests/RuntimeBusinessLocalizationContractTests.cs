using Xunit;

namespace SmartAttendance.Tests;

public sealed class RuntimeBusinessLocalizationContractTests
{
    [Fact]
    public void BusinessCatalog_IsAuthenticated_TenantScoped_AndVisibleOnly()
    {
        var root = FindRoot();

        var controller = Read(
            root,
            "SmartAttendance.Web",
            "Controllers",
            "CultureBusinessCatalogController.cs");

        Assert.Contains(
            "[Authorize]",
            controller,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "[AllowAnonymous]",
            controller,
            StringComparison.Ordinal);

        Assert.Contains(
            "[Route(\"Culture/BusinessCatalog\")]",
            controller,
            StringComparison.Ordinal);

        Assert.Contains(
            "ICompanyScopeProvider",
            controller,
            StringComparison.Ordinal);

        Assert.Contains(
            "scope.IsDeniedAll",
            controller,
            StringComparison.Ordinal);

        Assert.Contains(
            "allowedCompanyIds.Contains",
            controller,
            StringComparison.Ordinal);

        Assert.Contains(
            "AppearsOnPage",
            controller,
            StringComparison.Ordinal);

        Assert.Contains(
            "pair.Value.Count == 1",
            controller,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetCompanyNamesAsync",
            controller,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetEntityNamesAsync",
            controller,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetEmployeeBusinessDataAsync",
            controller,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BusinessCatalog_IsNotAddedToPublicPathPolicy()
    {
        var root = FindRoot();

        var policy = Read(
            root,
            "SmartAttendance.Web",
            "Infrastructure",
            "Security",
            "PublicPathPolicy.cs");

        Assert.DoesNotContain(
            "/culture/businesscatalog",
            policy,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeBridge_FetchesVisibleBusinessAliases_AndResolvesNestedTemplates()
    {
        var root = FindRoot();

        var script = Read(
            root,
            "SmartAttendance.Web",
            "wwwroot",
            "js",
            "zynora-runtime-localization.js");

        Assert.Contains(
            "fetch(\"/Culture/BusinessCatalog\"",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "method: \"POST\"",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "credentials: \"same-origin\"",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "collectArabicValues",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "templateFragmentKeys",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "translateTemplateFragments",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "translated !== key && arabicText.test(translated)",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "catalog[source] = aliases[source]",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "20260907-p4",
            script,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ما ينتظرك", "Needs your attention")]
    [InlineData("+ بدء عملية", "+ Start process")]
    [InlineData("+ فئة جديدة", "+ New category")]
    [InlineData("الفلاتر", "Filters")]
    public void HighImpactAuditKeys_AreCompiledIntoEnglishCatalog(
        string key,
        string translation)
    {
        var root = FindRoot();

        var resource = Read(
            root,
            "SmartAttendance.Web",
            "Resources",
            "SharedResource.en-US.resx");

        Assert.Contains(
            $"name=\"{key}\"",
            resource,
            StringComparison.Ordinal);

        Assert.Contains(
            $"<value>{translation}</value>",
            resource,
            StringComparison.Ordinal);
    }

    private static string Read(
        string root,
        params string[] parts) =>
        File.ReadAllText(
            Path.Combine(
                new[] { root }
                    .Concat(parts)
                    .ToArray()));

    private static string FindRoot()
    {
        var directory =
            new DirectoryInfo(
                Directory.GetCurrentDirectory());

        while (directory is not null &&
               !File.Exists(
                   Path.Combine(
                       directory.FullName,
                       "SmartAttendance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find SmartAttendance.slnx.");
    }
}