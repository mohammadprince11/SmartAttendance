using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Issue 14: علَم <c>SalaryItem.InGross</c> يجب أن يؤثّر فعلاً بالمحرك لا أن يكون
/// خانةً تجميلية. المحرك يبني خريطة InGross بالاسم ويتخطّى علاوة InGross=false من
/// الإجمالي (فلا تدخل أي وعاء). الافتراض true يُبقي سلوك اليوم.
/// حارس نصّي — الاحتساب الكامل يحتاج قاعدة، والعلَم مثبَّت هنا أنه مُستهلَك.
/// </summary>
public class SalaryItemInGrossTests
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
    public void Engine_ConsumesInGrossFlag_AndSkipsFalse()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollRunStore.cs"));

        Assert.Contains("allowanceInGrossByName", source);
        Assert.Contains(".First().InGross", source);
        // العلاوة غير الداخلة بالإجمالي تُتخطّى قبل تجميعها بأي وعاء.
        Assert.Contains("!inGross) continue", source);
    }
}
