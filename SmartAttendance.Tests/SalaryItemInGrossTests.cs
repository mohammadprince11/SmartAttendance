using System.IO;
using SmartAttendance.Web.Infrastructure.Hrms;
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

        Assert.Contains("ResolveAllowancePolicy(salaryItemsById, al.SalaryItemId)", source);
        Assert.Contains("if (!policy.InGross) continue", source);
        // العلاوة غير الداخلة بالإجمالي تُتخطّى قبل تجميعها بأي وعاء.
        Assert.DoesNotContain("allowanceInGrossByName", source);
    }

    [Fact]
    public void Rename_DoesNotChangeAllowancePolicy()
    {
        var before = new SalaryItemStore.SalaryItem
        {
            Id = 42, Name = "Housing", InGross = true, Prorated = true,
            Taxable = false, GosiEligible = true, OvertimeEligible = true,
            UnpaidLeaveEligible = false
        };
        var after = new SalaryItemStore.SalaryItem
        {
            Id = 42, Name = "Housing Allowance", InGross = true, Prorated = true,
            Taxable = false, GosiEligible = true, OvertimeEligible = true,
            UnpaidLeaveEligible = false
        };

        var oldPolicy = PayrollRunStore.ResolveAllowancePolicy(
            new Dictionary<int, SalaryItemStore.SalaryItem> { [before.Id] = before }, 42);
        var renamedPolicy = PayrollRunStore.ResolveAllowancePolicy(
            new Dictionary<int, SalaryItemStore.SalaryItem> { [after.Id] = after }, 42);

        Assert.Equal(oldPolicy, renamedPolicy);
    }
}
