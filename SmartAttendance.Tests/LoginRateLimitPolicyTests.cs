using SmartAttendance.Web.Infrastructure.Security;
using Xunit;

namespace SmartAttendance.Tests;

/// <summary>
/// حدّ معدّل محاولات الدخول (P1-7 بتدقيق الجاهزية).
///
/// <para><b>الفجوة</b>: قفل الحساب (5 محاولات) يحمي <b>حساباً واحداً</b>. رشُّ كلمة
/// مرور واحدة على ألف حساب لا يلمس أي عدّاد قفل. والقفل نفسه سلاح تعطيل: معرفة اسم
/// المستخدم تكفي لإقفال المدير. الحدّ بعنوان الطالب يغلق الاتجاهين.</para>
/// </summary>
public class LoginRateLimitPolicyTests
{
    [Theory]
    [InlineData("/account/login")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/webauthn/login/options")]
    [InlineData("/api/webauthn/login/verify")]
    public void LoginPaths_AreLimited(string path) =>
        Assert.True(LoginRateLimitPolicy.AppliesTo(path));

    [Theory]
    [InlineData("/ACCOUNT/Login")]
    [InlineData("/Api/Auth/Login")]
    public void MatchingIsCaseInsensitive(string path) =>
        Assert.True(LoginRateLimitPolicy.AppliesTo(path));

    /// <summary>
    /// الحدّ العام على كل النظام يخنق الاستعمال المشروع (شاشات الحضور تُحدَّث كثيراً)
    /// بلا مكسب أمنيّ — فما ليس مسار دخول يمرّ بلا قيد.
    /// </summary>
    [Theory]
    [InlineData("/dayattendance")]
    [InlineData("/api/me/attendance")]
    [InlineData("/payroll/runs")]
    [InlineData("/health/ready")]
    [InlineData("/")]
    public void EverythingElse_IsFree(string path) =>
        Assert.False(LoginRateLimitPolicy.AppliesTo(path));

    /// <summary>الخروج ليس مسار دخول — خنقه يمنع المستخدم من إنهاء جلسته.</summary>
    [Fact]
    public void LogoutIsNotLimited() =>
        Assert.False(LoginRateLimitPolicy.AppliesTo("/api/auth/logout"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingPath_IsNotLimited(string? path) =>
        Assert.False(LoginRateLimitPolicy.AppliesTo(path));

    // ═══ مفتاح التقسيم ═══

    [Fact]
    public void PartitionKey_IsTheCallerAddress() =>
        Assert.Equal("203.0.113.9", LoginRateLimitPolicy.PartitionKey("203.0.113.9"));

    /// <summary>
    /// <b>مغلق الفشل</b>: غياب العنوان يجمع كل المجهولين بدلوٍ واحد بدل إعفائهم —
    /// الإعفاء يجعل تزوير غياب العنوان طريق الالتفاف الأول.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingAddress_SharesOneBucket_NotExempt(string? address) =>
        Assert.Equal("unknown", LoginRateLimitPolicy.PartitionKey(address));

    /// <summary>عنوانان مختلفان لا يتقاسمان دلواً — وإلا خنق أحدهما الآخر.</summary>
    [Fact]
    public void DistinctAddresses_GetDistinctBuckets() =>
        Assert.NotEqual(
            LoginRateLimitPolicy.PartitionKey("198.51.100.1"),
            LoginRateLimitPolicy.PartitionKey("198.51.100.2"));

    /// <summary>
    /// الحدّ يجب أن يتّسع لخطأ إنسانيّ معقول (قفل الحساب عند 5) ويضيق دون الرشّ
    /// الآليّ. النافذة كذلك: قصيرة فلا تُعاقب مستخدماً نسي كلمته، وطويلة بما يكفي
    /// لجعل التجريب الآليّ مكلفاً.
    /// </summary>
    [Fact]
    public void Limits_LeaveRoomForHumanErrorButNotForSpraying()
    {
        Assert.True(LoginRateLimitPolicy.PermitLimit > 5,
            "الحدّ يجب أن يتجاوز عتبة قفل الحساب وإلا حجب المستخدم قبل أن يقفل حسابه.");
        Assert.True(LoginRateLimitPolicy.PermitLimit <= 20);
        Assert.InRange(LoginRateLimitPolicy.WindowMinutes, 1, 15);
    }
}
