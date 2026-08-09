using System;
using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Phase 8 (ذرّية وتزامن إنشاء المسير): كان <c>CreateRunAsync</c> يخصّص رقم الدفعة
/// بـ<c>COUNT(1)+1</c> بلا قفل (سباقٌ ⟹ رقمان متطابقان)، ويُدرج المسير ثم أعضاء
/// نطاقه في نداءين منفصلين بلا معاملة (فشل الثاني ⟹ Draft يتيم بلا أعضاء يُفسَّر
/// «كل الموظفين»).
///
/// <para>الإصلاح: معاملة واحدة تلفّ التخصيص والإدراجين، وقفل مدى
/// (<c>UPDLOCK, HOLDLOCK</c>) يسلسل المُنشئين المتزامنين. حرّاس نصّية نقيّة؛
/// اختبار التزامن الحقيقي (20 إنشاءً متوازياً) يبقى <b>DB-GATED</b>.</para>
/// </summary>
public class PayrollRunCreateAtomicityTests
{
    private static string CreateRunBody()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(
            dir!.FullName, "SmartAttendance.Web", "Infrastructure", "Hrms", "PayrollRunStore.cs"));
        var idx = source.IndexOf("CreateRunAsync(", StringComparison.Ordinal);
        Assert.True(idx > 0);
        var end = source.IndexOf("CalculateAsync", idx, StringComparison.Ordinal);
        Assert.True(end > idx);
        return source[idx..end];
    }

    /// <summary>🔴 الذرّية: الإدراجان داخل معاملة واحدة تُلتزَم صراحةً.</summary>
    [Fact]
    public void إنشاء_المسير_معاملة_واحدة()
    {
        var body = CreateRunBody();
        Assert.Contains("BeginTransactionAsync", body);
        Assert.Contains("CommitAsync", body);
    }

    /// <summary>🔴 التزامن: تخصيص الرقم يقفل المدى فلا يتكرّر تحت التوازي.</summary>
    [Fact]
    public void تخصيص_رقم_الدفعة_يقفل_المدى()
    {
        var body = CreateRunBody();
        Assert.Contains("UPDLOCK, HOLDLOCK", body);
        // لم يبقَ استعلام عدٍّ بلا قفل.
        Assert.DoesNotContain("FROM PayrollRuns WHERE [Year] = @Y AND [Month] = @M", body);
    }
}
