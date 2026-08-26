using System;
using System.Collections.Generic;
using System.IO;
using SmartAttendance.Web.Infrastructure.Hrms;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// القسم 1 (عنصر الراتب — القواعد + معايير الاستحقاق): حدّ القيمة الدنيا/القصوى،
/// فترة الصلاحية، والشروط المؤهِّلة. <b>القاعدة الذهبية: كل الحقول اختيارية —
/// null/فارغ ⟹ السلوك القديم تماماً</b> (لا انحراف لمن لم يُفعِّل الميزة). ومعها
/// حارسٌ نصّي يثبّت أنّ المسير يُنفِّذ الحقول لا أن تكون تجميلية.
/// </summary>
public class SalaryItemRulesTests
{
    private static SalaryItemStore.SalaryItem Item() => new() { Id = 1, Name = "بدل" };

    // ── Clamp: الحدّ الأدنى/الأقصى ──
    [Fact]
    public void Clamp_NoBounds_ReturnsValueUnchanged()
    {
        var item = Item(); // MinValue=MaxValue=null
        Assert.Equal(123.45m, item.Clamp(123.45m));
        Assert.Equal(-9m, item.Clamp(-9m));
        Assert.Equal(0m, item.Clamp(0m));
    }

    [Fact]
    public void Clamp_Min_RaisesBelowFloor_LeavesAbove()
    {
        var item = Item();
        item.MinValue = 100m;
        Assert.Equal(100m, item.Clamp(40m));   // مرفوع للحدّ الأدنى
        Assert.Equal(150m, item.Clamp(150m));  // فوق الحدّ ⟹ بلا تغيير
    }

    [Fact]
    public void Clamp_Max_LowersAboveCeiling_LeavesBelow()
    {
        var item = Item();
        item.MaxValue = 200m;
        Assert.Equal(200m, item.Clamp(500m));  // مخفوض للحدّ الأقصى
        Assert.Equal(150m, item.Clamp(150m));  // تحت الحدّ ⟹ بلا تغيير
    }

    [Fact]
    public void Clamp_MinAndMax_BracketsValue()
    {
        var item = Item();
        item.MinValue = 100m;
        item.MaxValue = 200m;
        Assert.Equal(100m, item.Clamp(50m));
        Assert.Equal(200m, item.Clamp(999m));
        Assert.Equal(150m, item.Clamp(150m));
    }

    // ── WithinValidity: فترة الصلاحية ──
    private static readonly DateOnly PeriodStart = new(2026, 8, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 8, 31);

    [Fact]
    public void WithinValidity_NoWindow_AlwaysTrue()
    {
        var item = Item(); // ValidFrom=ValidTo=null
        Assert.True(item.WithinValidity(PeriodStart, PeriodEnd));
    }

    [Fact]
    public void WithinValidity_WindowOverlapsPeriod_True()
    {
        var item = Item();
        item.ValidFrom = new DateOnly(2026, 1, 1);
        item.ValidTo = new DateOnly(2026, 12, 31);
        Assert.True(item.WithinValidity(PeriodStart, PeriodEnd));

        // يبدأ داخل الفترة
        item.ValidFrom = new DateOnly(2026, 8, 15);
        item.ValidTo = null;
        Assert.True(item.WithinValidity(PeriodStart, PeriodEnd));
    }

    [Fact]
    public void WithinValidity_WindowEndsBeforePeriod_False()
    {
        var item = Item();
        item.ValidTo = new DateOnly(2026, 7, 31); // انتهى قبل الفترة
        Assert.False(item.WithinValidity(PeriodStart, PeriodEnd));
    }

    [Fact]
    public void WithinValidity_WindowStartsAfterPeriod_False()
    {
        var item = Item();
        item.ValidFrom = new DateOnly(2026, 9, 1); // يبدأ بعد الفترة
        Assert.False(item.WithinValidity(PeriodStart, PeriodEnd));
    }

    // ── EligibleFor: معايير الاستحقاق ──
    private static Dictionary<string, HrConditions.Fact> Facts(string gender) =>
        new() { ["gender"] = HrConditions.Fact.Of(gender) };

    [Fact]
    public void EligibleFor_NoConditions_MatchesEveryone()
    {
        var item = Item(); // EligibilityJson=null
        Assert.True(item.EligibleFor(Facts("ذكر")));
        Assert.True(item.EligibleFor(new Dictionary<string, HrConditions.Fact>()));
    }

    [Fact]
    public void EligibleFor_ConditionMatches_True_AndMismatch_False()
    {
        var set = new HrConditions.ConditionSet
        {
            Groups = new List<HrConditions.Group>
            {
                new()
                {
                    Rules = new List<HrConditions.Rule>
                    {
                        new() { Criterion = "gender", Op = HrConditions.Operator.Equal, Value = "ذكر" }
                    }
                }
            }
        };
        var item = Item();
        item.EligibilityJson = HrConditions.Serialize(set);

        Assert.True(item.EligibleFor(Facts("ذكر")));
        Assert.False(item.EligibleFor(Facts("أنثى")));
    }

    // مسار جذر المستودع من موقع ملف الاختبار وقت التصريف — مستقلٌّ عن مجلّد العمل
    // (يعمل حتى عند إعادة توجيه مخرجات البناء لمجلّد خارج المستودع).
    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(path)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ── حارس نصّي: المسير يُنفِّذ القواعد والاستحقاق (لا حقولٌ تجميلية) ──
    [Fact]
    public void Engine_EnforcesValidityEligibilityAndClamp()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollRunStore.cs"));

        // حلقة العلاوات: تخطّي غير الساري/غير المؤهّل + حصر المبلغ.
        Assert.Contains("!sItem.WithinValidity(periodStart, periodEnd) || !sItem.EligibleFor(facts)", source);
        Assert.Contains("var convertedAllowance = fx.Convert(al.Amount)", source);
        Assert.Contains("sItem?.Clamp(convertedAllowance)", source);
        // حلقة الصيغ: تخطّي غير الساري/غير المؤهّل + حصر القيمة.
        Assert.Contains("!item.WithinValidity(periodStart, periodEnd) || !item.EligibleFor(facts)", source);
        Assert.Contains("item.Clamp(Math.Round(item.Prorated ? raw * factor : raw, 2))", source);
    }
}
