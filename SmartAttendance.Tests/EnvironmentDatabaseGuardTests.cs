using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

public class EnvironmentDatabaseGuardTests
{
    private const string Primary =
        "Server=localhost;Database=SmartAttendance;Trusted_Connection=True";

    private const string RemotePrimary =
        "Server=sql.example.internal;Database=SmartAttendance;Trusted_Connection=True";

    private const string Dev =
        "Server=localhost;Database=SmartAttendance_Dev;Trusted_Connection=True";

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData(null)]
    public void NonProduction_OnPrimaryDatabase_IsRejectedByDefault(
        string? environment)
    {
        var refusal = EnvironmentDatabaseGuard.Validate(
            environment,
            Primary);

        Assert.NotNull(refusal);
        Assert.Contains("SmartAttendance", refusal);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    public void Production_OnPrimaryDatabase_IsAllowed(string environment)
    {
        Assert.Null(
            EnvironmentDatabaseGuard.Validate(environment, Primary));
    }

    [Fact]
    public void Development_OnSeparateDatabase_IsAllowed()
    {
        Assert.Null(
            EnvironmentDatabaseGuard.Validate(
                "Development",
                Dev));
    }

    [Fact]
    public void LocalDevelopment_CanExplicitlyUsePrimaryDatabase()
    {
        Assert.Null(
            EnvironmentDatabaseGuard.Validate(
                "Development",
                Primary,
                allowLocalDevelopmentOnPrimaryDatabase: true));
    }

    [Fact]
    public void ExplicitOverride_DoesNotAllowStaging()
    {
        Assert.NotNull(
            EnvironmentDatabaseGuard.Validate(
                "Staging",
                Primary,
                allowLocalDevelopmentOnPrimaryDatabase: true));
    }

    [Fact]
    public void ExplicitOverride_DoesNotAllowRemoteSqlServer()
    {
        Assert.NotNull(
            EnvironmentDatabaseGuard.Validate(
                "Development",
                RemotePrimary,
                allowLocalDevelopmentOnPrimaryDatabase: true));
    }

    [Theory]
    [InlineData("Server=localhost;Database=SmartAttendance;Trusted_Connection=True")]
    [InlineData("Server=127.0.0.1;Database=SmartAttendance;Trusted_Connection=True")]
    [InlineData(@"Server=.\SQLEXPRESS;Database=SmartAttendance;Trusted_Connection=True")]
    [InlineData(@"Server=localhost\SQLEXPRESS;Database=SmartAttendance;Trusted_Connection=True")]
    public void LocalSqlServer_IsRecognized(string connectionString)
    {
        Assert.True(
            EnvironmentDatabaseGuard.IsLocalSqlServer(connectionString));
    }

    [Fact]
    public void RemoteSqlServer_IsNotLocal()
    {
        Assert.False(
            EnvironmentDatabaseGuard.IsLocalSqlServer(RemotePrimary));
    }

    [Fact]
    public void ExactDatabaseMatch_NotPrefix()
    {
        Assert.Null(
            EnvironmentDatabaseGuard.Validate(
                "Development",
                "Server=localhost;Database=SmartAttendanceDB;Trusted_Connection=True"));
    }

    [Fact]
    public void InitialCatalog_IsRecognized()
    {
        Assert.NotNull(
            EnvironmentDatabaseGuard.Validate(
                "Development",
                "Server=localhost;Initial Catalog=SmartAttendance;Integrated Security=true"));
    }

    [Fact]
    public void DatabaseName_IsCaseInsensitive()
    {
        Assert.NotNull(
            EnvironmentDatabaseGuard.Validate(
                "Development",
                "Server=localhost;Database=smartattendance;Trusted_Connection=True"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Server=localhost;Trusted_Connection=True")]
    public void AmbiguousConnectionString_DoesNotBlockStartup(
        string? connectionString)
    {
        Assert.Null(
            EnvironmentDatabaseGuard.Validate(
                "Development",
                connectionString));
    }

    [Fact]
    public void DatabaseName_IsExtracted()
    {
        Assert.Equal(
            "SmartAttendance_Dev",
            EnvironmentDatabaseGuard.DatabaseName(Dev));

        Assert.Null(
            EnvironmentDatabaseGuard.DatabaseName(null));
    }
}