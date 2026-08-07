using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حارس «مصدر حقيقة الحضور»: مؤشّرات الحضور/التأخير/الغياب تُقرأ من اليوميات
/// المحلَّلة (<c>DayAttendances</c>) لا من عمود <c>AttendanceRecords.Status</c>.
///
/// <para><b>العطل الذي يحرسه</b> (رُصد بالكود 2026-08-07): العمود
/// <c>AttendanceRecords.Status</c> <b>صامّ</b> — كل مسار كتابةٍ يختمه
/// <c>Present</c> ثابتاً: الاستيراد الدفعي يمرّر <c>(int)AttendanceStatus.Present</c>،
/// والبصمة الأونلاين تكتب <c>Status</c> بقيمة <c>1</c> نصّاً بالـSQL. ومع ذلك كانت
/// شاشتان (<c>/Employees/Profile</c> و<c>/MyProfile</c>) تحسبان منه «حاضر/متأخر/غائب»
/// بـ<c>WHERE Status = 1/2/3</c> ⟹ التأخير والغياب <b>صفر دائماً</b> بملف كل موظف
/// وبوابته، بينما <c>/DayAttendance</c> يعرض القيم الحقيقية — حقيقتان متناقضتان.</para>
///
/// <para>ولم يكن الأثر عرضياً: <c>AbsentCount</c> يغذّي <c>PayrollRiskItems</c>
/// ومؤشّر صحة الموظف (<c>score -= AbsentCount * 8</c>)، فكان مصدر الخصم صفراً بنيوياً.</para>
///
/// <para>الاختبار نصّيّ عمداً: الشاشتان صفحتا Razor تقرآن قاعدةً حيّة، والقيمة هنا
/// أن يفشل البناء إن عاد أحدٌ يشتقّ حكماً من عمودٍ لا يحمل حكماً — لا أن تُحاكى
/// القاعدة.</para>
/// </summary>
public class AttendanceTruthSourceTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string WebRoot() => Path.Combine(RepoRoot(), "SmartAttendance.Web");

    private static string[] SourceFiles() =>
        Directory.GetFiles(WebRoot(), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(WebRoot(), "*.cshtml", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToArray();

    /// <summary>لا استعلام يشتقّ حكماً من العمود الصامّ.</summary>
    [Fact]
    public void NoScreenDerivesAttendanceJudgementFromTheDeadStatusColumn()
    {
        // استعلامٌ يذكر AttendanceRecords ثم يرشّح بـStatus = رقم. الرقم شرطٌ مقصود:
        // `Status = N'Present'` على DayAttendances مسموح، والممنوع هو enum البصمة الخام.
        var offender = new Regex(
            @"FROM\s+AttendanceRecords\b[^;""]*?\bStatus\s*=\s*\d",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var hits = SourceFiles()
            .Where(path => offender.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(RepoRoot(), path))
            .ToList();

        Assert.True(
            hits.Count == 0,
            "هذه الملفات تشتقّ حكم حضورٍ من `AttendanceRecords.Status` وهو عمود صامّ "
            + "(يُختم Present ثابتاً بكل مسار كتابة) — المصدر الصحيح `DayAttendances`: "
            + string.Join(" · ", hits));
    }

    /// <summary>الشاشتان المُصحَّحتان تقرآن اليوميات فعلاً — لا مجرّد خلوّهما من العطل.</summary>
    [Theory]
    [InlineData("Pages/Employees/Profile.cshtml.cs")]
    [InlineData("Pages/MyProfile/Index.cshtml.cs")]
    public void ProfileScreensCountFromAnalysedDays(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(WebRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Matches(@"FROM\s+DayAttendances\b[^;""]*Status\s*=\s*N'Present'", source);
        Assert.Matches(@"FROM\s+DayAttendances\b[^;""]*Status\s*=\s*N'Late'", source);

        // الجدول يُنشأ كسولاً — بلا الحارس تفشل الشاشة كلها بقاعدةٍ لم يُشغَّل عليها
        // التحليل بعد، وهو أسوأ من العطل الذي عولج.
        Assert.Contains("DayAttendanceStore.EnsureAsync", source);
    }
}
