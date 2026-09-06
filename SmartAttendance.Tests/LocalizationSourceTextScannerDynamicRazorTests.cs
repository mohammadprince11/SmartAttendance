using SmartAttendance.Web.Infrastructure.Localization;
using Xunit;

namespace SmartAttendance.Tests;

public sealed class LocalizationSourceTextScannerDynamicRazorTests
{
    [Fact]
    public void Scanner_DiscoversArabicLiteralsInsideDynamicRazorExpressions()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "zynora-localization-scanner-" + Guid.NewGuid().ToString("N"));

        var pages = Path.Combine(root, "Pages");
        Directory.CreateDirectory(pages);

        try
        {
            File.WriteAllText(
                Path.Combine(pages, "Profile.cshtml"),
                """
                @page
                @{
                    var note = row is null
                        ? "لا سياسة إجازات"
                        : $"يوم سنوي متبقٍّ — {Model.LeaveLedgerYear}";
                }

                <div>
                    @(row is null
                        ? "لا سياسة إجازات"
                        : $"يوم سنوي متبقٍّ — {Model.LeaveLedgerYear}")
                </div>

                @functions {
                    private static string WorkDuration(int years, int months)
                    {
                        if (years <= 0)
                            return $"{months} شهر";

                        return $"{years} سنة و {months} شهر";
                    }
                }
                """);

            var keys = LocalizationSourceTextScanner.Scan(root);

            Assert.Contains("لا سياسة إجازات", keys);
            Assert.Contains("يوم سنوي متبقٍّ — {0}", keys);
            Assert.Contains("{0} شهر", keys);
            Assert.Contains("{0} سنة و {1} شهر", keys);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RealEmployeeProfile_DynamicLeaveNote_IsDiscoverable()
    {
        var root = FindRoot();
        var webRoot = Path.Combine(root, "SmartAttendance.Web");

        var keys = LocalizationSourceTextScanner.Scan(webRoot);

        Assert.Contains("لا سياسة إجازات", keys);
        Assert.Contains("يوم سنوي متبقٍّ — {0}", keys);
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
            ?? throw new DirectoryNotFoundException(
                "Could not find SmartAttendance.slnx.");
    }
}