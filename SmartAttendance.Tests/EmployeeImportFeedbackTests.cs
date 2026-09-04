using SmartAttendance.Web.Infrastructure.Imports;

namespace SmartAttendance.Tests;

public sealed class EmployeeImportFeedbackTests
{
    [Fact]
    public void FailureMessage_ExplainsTheMostCommonMissingFieldInArabic()
    {
        var message = EmployeeBootstrapImportEngine.BuildImportFailureMessage(
            new[]
            {
                new[] { "DepartmentName is required." },
                new[] { "DepartmentName is required." },
                new[] { "HireDate is required." }
            });

        Assert.Contains("لم يتم استيراد أي موظف", message);
        Assert.Contains("الصفوف المرفوضة: 3", message);
        Assert.Contains("اسم القسم مطلوب (2 صف)", message);
        Assert.Contains("تاريخ التعيين مطلوب (1 صف)", message);
        Assert.DoesNotContain("DepartmentName", message);
    }

    [Fact]
    public void FailureMessage_ExplainsWhenTheWorkbookHasNoEmployeeRows()
    {
        var message = EmployeeBootstrapImportEngine.BuildImportFailureMessage(
            Array.Empty<IEnumerable<string>>());

        Assert.Equal(
            "لم يعثر الملف على صفوف موظفين قابلة للاستيراد.",
            message);
    }

    [Fact]
    public void EmployeeImportPage_DoesNotPresentARejectedImportAsSuccess()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "SmartAttendance.Web",
            "Pages",
            "Employees",
            "Index.cshtml.cs"));

        Assert.Contains(
            "result.CreatedCount > 0 || result.UpdatedCount > 0",
            source,
            StringComparison.Ordinal);
        Assert.Contains("? \"SuccessMessage\"", source, StringComparison.Ordinal);
        Assert.Contains(": \"ErrorMessage\"", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SmartAttendance.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
