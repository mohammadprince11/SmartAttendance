using Xunit;

namespace SmartAttendance.Tests;

public sealed class E2EBootstrapLocalizationSchemaContractTests
{
    [Fact]
    public void DisposableBootstrap_AppliesExplicitLocalizationSchema()
    {
        var root = FindRoot();

        var bootstrap = File.ReadAllText(
            Path.Combine(
                root,
                "tools",
                "SmartAttendance.E2E.Bootstrap",
                "Program.cs"));

        var companyConfiguration = File.ReadAllText(
            Path.Combine(
                root,
                "SmartAttendance.Infrastructure",
                "Persistence",
                "Configurations",
                "CompanyLanguageConfiguration.cs"));

        var localizedValueConfiguration = File.ReadAllText(
            Path.Combine(
                root,
                "SmartAttendance.Infrastructure",
                "Persistence",
                "Configurations",
                "LocalizedEntityValueConfiguration.cs"));

        Assert.Contains(
            "ExcludeFromMigrations()",
            companyConfiguration,
            StringComparison.Ordinal);

        Assert.Contains(
            "ExcludeFromMigrations()",
            localizedValueConfiguration,
            StringComparison.Ordinal);

        Assert.Contains(
            "20260828-01-tenant-business-data-localization.sql",
            bootstrap,
            StringComparison.Ordinal);

        Assert.Contains(
            "File.ReadAllTextAsync(localizationMigrationPath)",
            bootstrap,
            StringComparison.Ordinal);

        var generatedSchema =
            bootstrap.IndexOf(
                "GenerateCreateScript()",
                StringComparison.Ordinal);

        var explicitLocalizationSchema =
            bootstrap.IndexOf(
                "20260828-01-tenant-business-data-localization.sql",
                StringComparison.Ordinal);

        Assert.True(generatedSchema >= 0);
        Assert.True(explicitLocalizationSchema > generatedSchema);
    }

    [Fact]
    public void ExplicitLocalizationMigration_CreatesBothExcludedTables()
    {
        var root = FindRoot();

        var migration = File.ReadAllText(
            Path.Combine(
                root,
                "database",
                "migrations",
                "20260828-01-tenant-business-data-localization.sql"));

        Assert.Contains(
            "CREATE TABLE dbo.CompanyLanguages",
            migration,
            StringComparison.Ordinal);

        Assert.Contains(
            "CREATE TABLE dbo.LocalizedEntityValues",
            migration,
            StringComparison.Ordinal);
    }

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