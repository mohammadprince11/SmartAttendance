using System;
using System.IO;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// Phase 15 (مسار مصادقة الـAPI الساخن): كان <c>ApiTokenStore.ValidateAsync</c> —
/// المُنادى بكل طلب Bearer — يستدعي <c>EnsureAsync</c> الذي ينفّذ DDL
/// (<c>IF OBJECT_ID('ApiTokens') IS NULL CREATE TABLE…</c>) في كل مرّة.
///
/// <para>الإصلاح: يُضمَن المخطط مرّة واحدة عند الإقلاع (<c>Program</c>)، ويصير
/// التحقّق بذرةً مفهرسة محدودة على <c>TokenHash</c>. هذه حرّاس نصّية نقيّة بلا SQL.</para>
/// </summary>
public class ApiTokenHotPathTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartAttendance.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }

    /// <summary>🔴 الانحدار: جسم ValidateAsync يجب ألّا يستدعي EnsureAsync (لا DDL بالمسار الساخن).</summary>
    [Fact]
    public void ValidateAsync_لا_ينفّذ_DDL_بالمسار_الساخن()
    {
        var source = RepoFile("SmartAttendance.Web", "Infrastructure", "Api", "ApiTokenStore.cs");
        var idx = source.IndexOf("Task<TokenIdentity?> ValidateAsync", StringComparison.Ordinal);
        Assert.True(idx > 0);
        var end = source.IndexOf("public static async Task RevokeAsync", idx, StringComparison.Ordinal);
        Assert.True(end > idx);
        var body = source[idx..end];

        Assert.DoesNotContain("EnsureAsync", body);
        // ما زال التحقّق يفلتر الإلغاء والانتهاء على البذرة المفهرسة.
        Assert.Contains("RevokedAt IS NULL", body);
        Assert.Contains("ExpiresAt > SYSUTCDATETIME()", body);
    }

    /// <summary>الإقلاع يضمن مخطط ApiTokens ضمن نطاق الهجرة (بذرة مرّة واحدة).</summary>
    [Fact]
    public void الإقلاع_يضمن_مخطط_ApiTokens()
    {
        var program = RepoFile("SmartAttendance.Web", "Program.cs");
        Assert.Contains("ApiTokenStore.EnsureAsync", program);
    }
}
