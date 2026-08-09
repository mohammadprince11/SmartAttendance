using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Issue 16: معاينة وعاء الضمان بشاشة المعلومات المالية يجب أن تحسم الوعاء بنفس
/// منطق المسير (<c>EmployeeDefinedSalaryBase.Resolve</c>) لا بالتركيب المباشر —
/// وإلا عرضت رقماً (2,000,000) يخالف ما يحتسبه المسير (400,000) عند
/// «مُعرَّف بالموظف». حارس نصّي على مصدر الصفحة.
/// </summary>
public class FinancialInfoPreviewAlignmentTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void GosiPreview_ResolvesThroughEmployeeDefinedBase()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Pages", "Employees", "FinancialInfo.cshtml.cs"));

        var compose = source.IndexOf("GosiBase", System.StringComparison.Ordinal);
        Assert.True(compose > 0);

        // يجب أن يُستدعى الحاسم بنفس نمط/قيمة المسير، لا Compose وحده للعرض.
        Assert.Contains("EmployeeDefinedSalaryBase.Resolve", source);
        Assert.Contains("Input.GosiBaseMode", source);
        Assert.Contains("Input.SocialSecuritySalary", source);
        Assert.Contains("GosiBase = gosiResolved.Base", source);
    }
}
