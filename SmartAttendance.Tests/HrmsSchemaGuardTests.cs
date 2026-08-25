using System;
using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// انحدار الأداء (مسار القراءة الساخن): كان <c>HrmsDatabase.EnsureCreatedAsync</c> —
/// المُنادى من ٥٧ موضعاً في <c>OnGet</c>/<c>OnPost</c> — ينفّذ سكربت شفاء ذاتي من
/// ٢٢٦ سطر DDL (<c>IF COL_LENGTH… ALTER TABLE</c>, <c>IF OBJECT_ID IS NULL CREATE TABLE</c>)
/// في كل طلب، بلا أي حارس. هذا هدرٌ على كل صفحة وخرقٌ لقاعدة «لا شفاء ذاتي على الطلب».
///
/// <para>الإصلاح: حارس تشغيل-مرّة-واحدة لكل عملية (<c>_schemaEnsured</c> + قفل مزدوج
/// الفحص عبر <c>SemaphoreSlim</c>)، والـDDL نُقل لدالة خاصّة <c>RunSchemaScriptAsync</c>
/// لا تُستدعى إلا مرّة واحدة بعد أول طلب. السكربت idempotent فالتشغيل مرّة آمن.</para>
///
/// <para>حرّاس نصّية نقيّة على المصدر — نفس نمط <see cref="ApiTokenHotPathTests"/> (Phase 15).</para>
/// </summary>
public class HrmsSchemaGuardTests
{
    private static string HrmsSource()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "SmartAttendance.Web", "Infrastructure", "Hrms", "HrmsDatabase.cs"));
    }

    private static string Body(string source, string signature, string nextSignature)
    {
        var idx = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(idx > 0, $"لم يُعثر على «{signature}»");
        var end = source.IndexOf(nextSignature, idx, StringComparison.Ordinal);
        Assert.True(end > idx, $"لم يُعثر على «{nextSignature}» بعد «{signature}»");
        return source[idx..end];
    }

    /// <summary>🔴 الانحدار: نقطة الدخول تحرس بعَلَم وتقصُر التنفيذ، ولا تحمل الـDDL مباشرةً.</summary>
    [Fact]
    public void EnsureCreatedAsync_يحرس_بتشغيل_مرّة_واحدة()
    {
        var source = HrmsSource();
        var body = Body(source, "Task EnsureCreatedAsync", "private static async Task RunSchemaScriptAsync");

        // قصرٌ مبكّر بلا انتظار قفل عند ضمانٍ سابق.
        Assert.Contains("if (_schemaEnsured) return;", body);
        // يُضبط العَلَم بعد النجاح فقط ⟹ فشلٌ جزئي يُعاد.
        Assert.Contains("_schemaEnsured = true;", body);
        // القفل يمنع سباق أول طلبين متزامنين.
        Assert.Contains("_ensureGate.WaitAsync", body);
        // 🔴 لا DDL على نقطة الدخول — نُقل بالكامل لدالة السكربت.
        Assert.DoesNotContain("ALTER TABLE", body);
        Assert.DoesNotContain("CREATE TABLE", body);
    }

    /// <summary>الحقول: عَلَم volatile وقفل SemaphoreSlim معرّفان على الصنف.</summary>
    [Fact]
    public void حقول_الحارس_معرّفة()
    {
        var source = HrmsSource();
        Assert.Contains("private static volatile bool _schemaEnsured", source);
        Assert.Contains("SemaphoreSlim _ensureGate", source);
    }

    /// <summary>سكربت الشفاء الذاتي ما زال موجوداً — لكنه معزول في دالته الخاصّة (يُشغَّل مرّة).</summary>
    [Fact]
    public void سكربت_المخطط_محفوظ_ومعزول()
    {
        var source = HrmsSource();
        var script = Body(source, "private static async Task RunSchemaScriptAsync", "public static async Task ExecuteAsync");

        // لم يُحذف أي شفاء ذاتي — فقط عُزل خلف الحارس.
        Assert.Contains("ALTER TABLE Employees ADD Position", script);
        Assert.Contains("IF OBJECT_ID('SelfServiceRequests', 'U') IS NULL", script);
        Assert.Contains("await ExecuteAsync(dbContext, sql);", script);
    }

    [Fact]
    public void الجدول_الاختياري_لا_يُربط_الدفعة_قبل_شرط_وجوده()
    {
        var source=HrmsSource();
        var optionalStart=source.IndexOf("IF OBJECT_ID('PunchSemantics', 'U') IS NOT NULL",StringComparison.Ordinal);
        var optionalEnd=source.IndexOf("IF OBJECT_ID('ApprovalHistories'",optionalStart,StringComparison.Ordinal);
        Assert.True(optionalStart>0&&optionalEnd>optionalStart);
        var block=source[optionalStart..optionalEnd];
        Assert.Contains("EXEC sp_executesql",block,StringComparison.Ordinal);
        Assert.DoesNotContain("AND EXISTS (SELECT 1 FROM AttendanceRecords",block,StringComparison.Ordinal);
    }
}
