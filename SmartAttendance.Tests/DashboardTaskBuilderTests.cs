using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// «مهامك اليوم»: ما يظهر، وبأي ترتيب. القاعدة الحاكمة — لوحة تعرض أصفاراً
/// تُدرَّب على تجاهلها، فالمهمة بعدّاد صفر لا تظهر إطلاقاً.
/// </summary>
public class DashboardTaskBuilderTests
{
    private static DashboardTaskBuilder.Counts Counts(
        int requests = 0, int keys = 0, int contracts = 0, int absence = 0) =>
        new(requests, keys, contracts, absence);

    [Fact]
    public void NothingPending_ProducesNoTasks() =>
        Assert.Empty(DashboardTaskBuilder.Build(Counts()));

    [Fact]
    public void ZeroCounters_AreNeverRendered()
    {
        var tasks = DashboardTaskBuilder.Build(Counts(requests: 3));

        Assert.Single(tasks);
        Assert.Equal("approvals", tasks[0].Key);
    }

    /// <summary>الأمن أسبق: مفتاح بصمة معلّق قبل الموافقات قبل العقود.</summary>
    [Fact]
    public void Tasks_AreOrderedByUrgency()
    {
        var tasks = DashboardTaskBuilder.Build(
            Counts(requests: 5, keys: 2, contracts: 9, absence: 4));

        Assert.Equal(
            new[] { "biometric", "approvals", "absence", "contracts" },
            tasks.Select(t => t.Key).ToArray());
    }

    [Fact]
    public void EachTask_CarriesACountAndDestination()
    {
        var task = DashboardTaskBuilder.Build(Counts(keys: 2))[0];

        Assert.Equal(2, task.Count);
        Assert.Equal("/BiometricKeys", task.Url);
        Assert.Equal(DashboardTaskBuilder.TaskTone.Danger, task.Tone);
        Assert.False(string.IsNullOrWhiteSpace(task.ActionLabel));
    }

    [Fact]
    public void ApprovalsTask_PointsAtTheApprovalsPage()
    {
        var task = DashboardTaskBuilder.Build(Counts(requests: 1))[0];

        Assert.Equal("/Approvals", task.Url);
        Assert.Contains("1", task.Title);
    }

    // ===== نص الحالة =====

    [Fact]
    public void Headline_WhenClear_SaysSo() =>
        Assert.Equal("لا شيء ينتظر قرارك الآن",
            DashboardTaskBuilder.BuildHeadline(DashboardTaskBuilder.Build(Counts())));

    [Fact]
    public void Headline_UsesSingularForOneTask() =>
        Assert.Contains("أمر يحتاج",
            DashboardTaskBuilder.BuildHeadline(DashboardTaskBuilder.Build(Counts(requests: 4))));

    [Fact]
    public void Headline_UsesPluralForSeveralTasks() =>
        Assert.Contains("أمور تحتاج",
            DashboardTaskBuilder.BuildHeadline(
                DashboardTaskBuilder.Build(Counts(requests: 4, keys: 1))));

    /// <summary>عدد المهام لا يساوي مجموع العدّادات — بل عدد الأمور المختلفة.</summary>
    [Fact]
    public void Headline_CountsTaskKindsNotRecords() =>
        Assert.StartsWith("2 ",
            DashboardTaskBuilder.BuildHeadline(
                DashboardTaskBuilder.Build(Counts(requests: 40, contracts: 12))));
}
