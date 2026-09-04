using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حرّاس انحدار ضد DOM XSS (Phase 10): شاشات كانت تحقن اسم/رقم الموظف أو نصّ خيارٍ
/// يكتبه المستخدم داخل <c>innerHTML</c> نصّاً — اسمٌ مثل <c>&lt;img src=x
/// onerror=...&gt;</c> يُنفَّذ. الإصلاح: بناء DOM آمن (<c>textContent</c>). هذه
/// الاختبارات تفشل قبل الإصلاح وتنجح بعده.
/// </summary>
public sealed class DomXssGuardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Js(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "SmartAttendance.Web", "wwwroot", "js", file));

    [Fact]
    public void EmpPicker_RendersEmployeeNameAsTextNotHtml()
    {
        var js = Js("emp-picker.js");
        Assert.Contains("nm.textContent = e.Name", js);
        // لم يعد اسم الموظف يُدمَج داخل innerHTML.
        Assert.DoesNotContain("empk-nm\">' + (e.Name", js);
    }

    [Fact]
    public void MassScope_RendersEmployeeNameAsTextNotHtml()
    {
        var js = Js("mass-scope.js");
        Assert.Contains("nm.textContent = e.Name", js);
        Assert.DoesNotContain("class=\"nm\">' + (e.Name", js);
    }

    [Fact]
    public void AnnouncementStudio_RendersPollOptionAsTextNotHtml()
    {
        var js = Js("zynora-announcement-studio-dynamic.js");
        Assert.Contains("label.textContent = opt", js);
        Assert.DoesNotContain("<small>${opt}</small>", js);
    }
}
