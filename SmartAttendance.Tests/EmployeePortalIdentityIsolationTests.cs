using Xunit;

namespace SmartAttendance.Tests;

/// <summary>حارس P0: غياب ربط الحساب لا يجوز أن ينتحل أول موظف في قاعدة البيانات.</summary>
public sealed class EmployeePortalIdentityIsolationTests
{
    [Fact]
    public void EmployeePortal_NeverFallsBackToFirstEmployee()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var portal = Path.Combine(directory!.FullName, "SmartAttendance.Web", "Pages", "EmployeePortal");
        var offenders = Directory.GetFiles(portal, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "SELECT TOP 1 Id FROM Employees ORDER BY Id", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }
}
