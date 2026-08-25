using Xunit;

namespace SmartAttendance.Tests;

public sealed class ApiVersionContractTests
{
    [Theory]
    [InlineData("AuthController.cs", "api/v1/auth")]
    [InlineData("MeController.cs", "api/v1/me")]
    [InlineData("WebAuthnController.cs", "api/v1/webauthn")]
    public void PublicApiControllers_ExposeExplicitV1Routes(string file, string route)
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "SmartAttendance.Web", "Controllers", "Api", file));
        Assert.Contains($"[Route(\"{route}\")]", source, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
