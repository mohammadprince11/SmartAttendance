using System.Text;

namespace SmartAttendance.Tests;

public class AttendanceOperationsEncodingTests
{
    [Fact]
    public void PageModel_UserFacingSource_HasNoCommonMojibakeMarkers()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var source = File.ReadAllText(Path.Combine(
            dir!.FullName, "SmartAttendance.Web", "Pages", "AttendanceOperations", "Index.cshtml.cs"),
            Encoding.UTF8);

        foreach (var marker in new[] { "Ã", "Â", "Ø", "Ù", "�" })
            Assert.DoesNotContain(marker, source, StringComparison.Ordinal);
    }
}
