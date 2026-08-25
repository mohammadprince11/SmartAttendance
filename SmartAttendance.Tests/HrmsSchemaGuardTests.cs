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
/// <para>الإصلاح: حارس تشغيل-مرّة-واحدة لكل قاعدة (قاموس قواعد + قفل لكل قاعدة
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
        Assert.Contains("EnsuredDatabases.ContainsKey(databaseKey)", body);
        // يُضبط العَلَم بعد النجاح فقط ⟹ فشلٌ جزئي يُعاد.
        Assert.Contains("EnsuredDatabases.TryAdd(databaseKey", body);
        // القفل يمنع سباق أول طلبين متزامنين.
        Assert.Contains("gate.WaitAsync", body);
        // 🔴 لا DDL على نقطة الدخول — نُقل بالكامل لدالة السكربت.
        Assert.DoesNotContain("ALTER TABLE", body);
        Assert.DoesNotContain("CREATE TABLE", body);
    }

    /// <summary>الحقول: قاموس حالة وقفل منفصل لكل قاعدة معرّفان على الصنف.</summary>
    [Fact]
    public void حقول_الحارس_معرّفة()
    {
        var source = HrmsSource();
        Assert.Contains("ConcurrentDictionary<string, byte> EnsuredDatabases", source);
        Assert.Contains("ConcurrentDictionary<string, SemaphoreSlim> EnsureGates", source);
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

    [Fact]
    public void مراقبو_الموافقات_لا_ينشئون_مفتاحا_إلى_جدول_طلبات_غائب()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(
            Assert.IsType<DirectoryInfo>(dir).FullName,
            "SmartAttendance.Web", "Infrastructure", "Hrms", "SqlSchemaMigrator.cs"));
        var migrationStart = source.IndexOf(
            "20260826-16-approval-policy-snapshots",
            StringComparison.Ordinal);
        Assert.True(migrationStart > 0);

        var block = source[migrationStart..];
        var parentGuard = block.IndexOf(
            "IF OBJECT_ID('SelfServiceRequests','U') IS NOT NULL",
            StringComparison.Ordinal);
        var watcherCreate = block.IndexOf(
            "CREATE TABLE ApprovalRequestWatchers",
            StringComparison.Ordinal);

        Assert.True(parentGuard >= 0 && watcherCreate > parentGuard);
    }

    [Fact]
    public void هجرة_المراحل_لا_تربط_العمود_قبل_إضافته()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx"))) dir = dir.Parent;
        var source=File.ReadAllText(Path.Combine(Assert.IsType<DirectoryInfo>(dir).FullName,
            "SmartAttendance.Web","Infrastructure","Hrms","SqlSchemaMigrator.cs"));
        var start=source.IndexOf("20260826-13-approval-parallel-stages",StringComparison.Ordinal);
        var end=source.IndexOf("20260826-14-approval-sla-reminders-alternates",start,StringComparison.Ordinal);
        Assert.True(start>0&&end>start);
        var block=source[start..end];
        Assert.Equal(2,block.Split("EXEC sp_executesql",StringSplitOptions.None).Length-1);
        Assert.DoesNotContain("\n UPDATE ApprovalTemplateSteps SET StageOrder",block,StringComparison.Ordinal);
        Assert.DoesNotContain("\n UPDATE ApprovalRequestSteps SET StageOrder",block,StringComparison.Ordinal);
    }
}
